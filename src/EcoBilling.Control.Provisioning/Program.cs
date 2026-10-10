using System.Text;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Administrators.CreateAdministrator;
using EcoBilling.Control.Application.Administrators.ResetAdministratorPassword;
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

var migrateOnly = args is ["migrate"];
var resetAdministratorPassword = args is ["reset-admin-password", _];

if (!migrateOnly && !resetAdministratorPassword && args.Length != 2)
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  EcoBilling.Control.Provisioning migrate");
    Console.Error.WriteLine("  EcoBilling.Control.Provisioning <email> <full-name>");
    Console.Error.WriteLine("  EcoBilling.Control.Provisioning reset-admin-password <email>");
    Console.Error.WriteLine("The password is prompted for separately and never accepted as a command-line argument.");

    return 1;
}

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var connectionString = configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException(
        "Missing required connection string 'ConnectionStrings:Database'. " +
        "Set it via the ConnectionStrings__Database environment variable.");

var services = new ServiceCollection();
services.AddDbContext<ControlDbContext>(options => options.UseNpgsql(
    connectionString,
    npgsql => npgsql.EnableRetryOnFailure(
        maxRetryCount: 3,
        maxRetryDelay: TimeSpan.FromSeconds(5),
        errorCodesToAdd: null)));
services.AddScoped<IAdministratorRepository, AdministratorRepository>();
services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
services.AddScoped<IUnitOfWork, EfUnitOfWork>();
services.AddSingleton<IHttpContextAccessor, ProvisioningHttpContextAccessor>();
services.AddScoped<IAuditWriter, AuditWriter>();
services.AddSingleton<IPasswordHasher, PasswordHasher>();
services.AddSingleton<IClock, SystemClock>();
services.AddScoped<ICommandHandler<CreateAdministratorCommand, CreateAdministratorResult>, CreateAdministratorHandler>();
services.AddScoped<ICommandHandler<ResetAdministratorPasswordCommand, ResetAdministratorPasswordResult>, ResetAdministratorPasswordHandler>();

await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
{
    ValidateOnBuild = true,
    ValidateScopes = true,
});
await using var scope = provider.CreateAsyncScope();

if (migrateOnly)
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
    await dbContext.Database.MigrateAsync();
    Console.WriteLine("Database migrations applied successfully.");

    return 0;
}

if (resetAdministratorPassword)
{
    var resetEmail = args[1];
    var newPassword = ReadPassword("New password (min 12 characters): ");
    var resetHandler = scope.ServiceProvider.GetRequiredService<
        ICommandHandler<ResetAdministratorPasswordCommand, ResetAdministratorPasswordResult>>();
    var resetResult = await resetHandler.HandleAsync(
        new ResetAdministratorPasswordCommand(resetEmail, newPassword),
        CancellationToken.None);

    if (resetResult.IsFailure)
    {
        Console.Error.WriteLine($"Failed: {resetResult.Error.Code} -- {resetResult.Error.Message}");

        return 1;
    }

    Console.WriteLine($"Reset password for administrator {resetResult.Value.NormalizedEmail} ({resetResult.Value.AdministratorId}).");

    return 0;
}

var email = args[0];
var fullName = args[1];
var password = ReadPassword("Password (min 12 characters): ");

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
static string ReadPassword(string prompt)
{
    Console.Write(prompt);

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

/// <summary>
/// Provisioning runs outside an HTTP request, so audit entries intentionally have no
/// request correlation, IP address or user-agent metadata.
/// </summary>
internal sealed class ProvisioningHttpContextAccessor : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; }
}
