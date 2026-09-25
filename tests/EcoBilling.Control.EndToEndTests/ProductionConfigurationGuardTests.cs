using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EcoBilling.Control.EndToEndTests;

/// <summary>
/// Verifies the Stage 15 startup guard: a Production deployment left at the wildcard
/// <c>AllowedHosts</c> default must fail to start, the same way a missing connection
/// string or signing key already does (Stage 4/6/8) -- rather than silently accepting
/// Host headers from anyone.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProductionConfigurationGuardTests(DatabaseFixture database)
{
    [Fact]
    public void StartingInProduction_WithTheWildcardAllowedHostsDefault_ThrowsAtStartup()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Database", database.ConnectionString);
            builder.UseSetting("Authentication:Jwt:SigningKey", "e2e-test-signing-key-at-least-32-bytes-long-hmac");
            builder.UseSetting(
                "ServiceAuth:Jwt:SigningKeyPem",
                "-----BEGIN EC PRIVATE KEY-----\nMHcCAQEEIM/Zmsn7TVLlASCs4fwrBHO+Z4+RMX0S/798BLR39S9soAoGCCqGSM49\nAwEHoUQDQgAErNpguOsMTV/j7ZfEW7Y4Lbtbl1gwdXQ7kEB9ZQWhTxZDRvANZoN2\n9tsaUpXl4W92V86YoF+I9ffOdEDkDNVhYA==\n-----END EC PRIVATE KEY-----");
            // AllowedHosts deliberately not overridden -- appsettings.json's base "*" applies.
        });

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void StartingInProduction_WithAllowedHostsExplicitlyConfigured_StartsNormally()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Database", database.ConnectionString);
            builder.UseSetting("Authentication:Jwt:SigningKey", "e2e-test-signing-key-at-least-32-bytes-long-hmac");
            builder.UseSetting(
                "ServiceAuth:Jwt:SigningKeyPem",
                "-----BEGIN EC PRIVATE KEY-----\nMHcCAQEEIM/Zmsn7TVLlASCs4fwrBHO+Z4+RMX0S/798BLR39S9soAoGCCqGSM49\nAwEHoUQDQgAErNpguOsMTV/j7ZfEW7Y4Lbtbl1gwdXQ7kEB9ZQWhTxZDRvANZoN2\n9tsaUpXl4W92V86YoF+I9ffOdEDkDNVhYA==\n-----END EC PRIVATE KEY-----");
            builder.UseSetting("AllowedHosts", "control.example.com");
        });

        var client = factory.CreateClient(); // must not throw.

        Assert.NotNull(client);
    }
}
