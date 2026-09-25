using System.Text;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Administrators.CreateAdministrator;
using EcoBilling.Control.Infrastructure;
using EcoBilling.Control.Infrastructure.Auditing;
using EcoBilling.Control.Infrastructure.Authentication;
using EcoBilling.Control.Infrastructure.Persistence;
using EcoBilling.Control.Infrastructure.Persistence.Administrators;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// A genuinely separate executable (Q4), not a flag branching inside Api's Program.cs:
// the point is keeping the password out of the always-running web process entirely,
// not just out of its normal request path. This is the ONLY way an administrator
// account is ever created -- there is no public self-registration and no admin-facing
// HTTP endpoint for it either. Authorization to run this tool is host/deployment
// access control (the same trust model as `dotnet ef database update`), not an
// additional secret -- adding one would defeat the point of moving provisioning out of
// the web process to avoid managing secrets in the first place.

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: EcoBilling.Control.Provisioning <email> <full-name>");
    Console.Error.WriteLine("The password is prompted for separately and never accepted as a command-line argument.");

    return 1;
}

var email = args[0];
var fullName = args[1];

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var connectionString = configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException(
        "Missing required connection string 'ConnectionStrings:Database'. " +
        "Set it via the ConnectionStrings__Database environment variable.");

var services = new ServiceCollection();
services.AddDbContext<ControlDbContext>(options => options.UseNpgsql(connectionString));
services.AddScoped<IAdministratorRepository, AdministratorRepository>();
services.AddScoped<IUnitOfWork, EfUnitOfWork>();
services.AddSingleton<IPasswordHasher, PasswordHasher>();
services.AddSingleton<IClock, SystemClock>();

// CreateAdministratorHandler audits every attempt (Stage 7) -- added here after this CLI
// was found broken (Stage 18 README verification): AuditWriter itself needs
// IHttpContextAccessor even though there is no HTTP request in this process at all; its
// own doc comment already anticipates that -- a null HttpContext just leaves
// CorrelationId/IpAddress/UserAgent null, no special-casing required.
services.AddHttpContextAccessor();
services.AddScoped<IAuditWriter, AuditWriter>();

services.AddScoped<ICommandHandler<CreateAdministratorCommand, CreateAdministratorResult>, CreateAdministratorHandler>();

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();

var password = ReadPassword();

var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateAdministratorCommand, CreateAdministratorResult>>();
var result = await handler.HandleAsync(new CreateAdministratorCommand(email, fullName, password), CancellationToken.None);

if (result.IsFailure)
{
    Console.Error.WriteLine($"Failed: {result.Error.Code} -- {result.Error.Message}");

    return 1;
}

Console.WriteLine($"Created administrator {result.Value.NormalizedEmail} ({result.Value.AdministratorId}).");

return 0;

// Reads the password with the terminal's echo suppressed, character by character, so
// it never appears on screen or in shell/terminal scrollback. Falls back to an
// unmasked ReadLine only when stdin is redirected (piped input, CI), where masking is
// impossible anyway and Console.ReadKey would throw.
static string ReadPassword()
{
    Console.Write("Password (min 12 characters): ");

    if (Console.IsInputRedirected)
    {
        return Console.ReadLine() ?? string.Empty;
    }

    var password = new StringBuilder();
    ConsoleKeyInfo key;

    do
    {
        key = Console.ReadKey(intercept: true);

        if (key.Key == ConsoleKey.Backspace && password.Length > 0)
        {
            password.Remove(password.Length - 1, 1);
            Console.Write("\b \b");
        }
        else if (!char.IsControl(key.KeyChar))
        {
            password.Append(key.KeyChar);
            Console.Write('*');
        }
    }
    while (key.Key != ConsoleKey.Enter);

    Console.WriteLine();

    return password.ToString();
}
