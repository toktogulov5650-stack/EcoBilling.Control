using System.Diagnostics;
using EcoBilling.Control.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EcoBilling.Control.EndToEndTests;

/// <summary>
/// Runs the actual, compiled <c>EcoBilling.Control.Provisioning</c> executable as a real
/// child process against a real PostgreSQL database -- the same rigor the
/// <c>WebApplicationFactory</c>-based tests elsewhere in this project apply to the Api
/// process, adapted to a console entry point that can't be hosted in-process the same
/// way. This exists because the CLI's own composition root (<c>Provisioning/Program.cs</c>)
/// was found broken by a manual README walkthrough (Stage 18) -- <c>CreateAdministratorHandler</c>
/// requires <c>IAuditWriter</c> (added Stage 7), which the CLI's hand-built
/// <c>ServiceCollection</c> never registered, and no existing test caught it because unit
/// tests construct the handler's dependencies by hand rather than through the CLI's own DI
/// setup. A missing registration here must now fail <c>dotnet test</c>, not require someone
/// to notice a stack trace on a real deployment.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProvisioningCliTests(DatabaseFixture database)
{
    [Fact]
    public async Task CreateAdministrator_RunAsTheRealCompiledExecutable_CreatesARealAdministratorRow()
    {
        const string email = "cli-e2e-admin@example.com";
        const string password = "a-real-cli-password-123";

        var startInfo = new ProcessStartInfo("dotnet", $"\"{FindProvisioningDllPath()}\" {email} \"CLI E2E Admin\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["ConnectionStrings__Database"] = database.ConnectionString;

        using var process = Process.Start(startInfo)!;

        // Program.cs falls back to an unmasked Console.ReadLine() whenever stdin is
        // redirected (its own documented behavior for piped/CI input) -- exactly the
        // case here, so a plain WriteLine is enough; no key-by-key masking to simulate.
        await process.StandardInput.WriteLineAsync(password);
        process.StandardInput.Close();

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.Equal(0, process.ExitCode); // fails exactly the way this bug did if a required registration goes missing again.
        Assert.Contains("Created administrator", stdout);
        Assert.Empty(stderr);

        var options = new DbContextOptionsBuilder<ControlDbContext>().UseNpgsql(database.ConnectionString).Options;
        await using var dbContext = new ControlDbContext(options);
        var created = await dbContext.Administrators.SingleOrDefaultAsync(a => a.NormalizedEmail == email);

        Assert.NotNull(created); // proves the row is real, not just that the process printed a success message.
    }

    /// <summary>
    /// Locates the real build output of <c>EcoBilling.Control.Provisioning</c> next to this
    /// test assembly's own build output, under the same Configuration (Debug/Release) --
    /// both are built together as part of the same <c>EcoBilling.Control.slnx</c>, which is
    /// how every build/test command in this repository is actually run.
    /// </summary>
    private static string FindProvisioningDllPath()
    {
        var netDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var configuration = netDirectory.Parent!.Name;

        var repositoryRoot = netDirectory;

        while (repositoryRoot is not null && !File.Exists(Path.Combine(repositoryRoot.FullName, "EcoBilling.Control.slnx")))
        {
            repositoryRoot = repositoryRoot.Parent;
        }

        if (repositoryRoot is null)
        {
            throw new InvalidOperationException(
                $"Could not locate EcoBilling.Control.slnx above {AppContext.BaseDirectory}.");
        }

        var dllPath = Path.Combine(
            repositoryRoot.FullName, "src", "EcoBilling.Control.Provisioning", "bin", configuration,
            netDirectory.Name, "EcoBilling.Control.Provisioning.dll");

        if (!File.Exists(dllPath))
        {
            throw new InvalidOperationException(
                $"EcoBilling.Control.Provisioning.dll not found at '{dllPath}'. This test assumes the whole " +
                "solution (EcoBilling.Control.slnx) was built, not just this test project.");
        }

        return dllPath;
    }
}
