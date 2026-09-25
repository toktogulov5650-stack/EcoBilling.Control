using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EcoBilling.Control.EndToEndTests;

/// <summary>
/// Verifies the Stage 18 CORS policy: permissive by default only in Development with
/// nothing configured, deny-by-default everywhere else, and an explicit
/// <c>Cors:AllowedOrigins</c> list always wins over both defaults.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CorsPolicyTests(DatabaseFixture database)
{
    private const string SigningKey = "e2e-test-signing-key-at-least-32-bytes-long-hmac";

    private const string SigningKeyPem =
        "-----BEGIN EC PRIVATE KEY-----\nMHcCAQEEIM/Zmsn7TVLlASCs4fwrBHO+Z4+RMX0S/798BLR39S9soAoGCCqGSM49\n" +
        "AwEHoUQDQgAErNpguOsMTV/j7ZfEW7Y4Lbtbl1gwdXQ7kEB9ZQWhTxZDRvANZoN2\n9tsaUpXl4W92V86YoF+I9ffOdEDkDNVhYA==\n" +
        "-----END EC PRIVATE KEY-----";

    private static async Task<string?> AllowOriginHeaderAsync(
        WebApplicationFactory<Program> factory, string requestOrigin)
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", requestOrigin);

        var response = await client.SendAsync(request);

        return response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.Single() : null;
    }

    [Fact]
    public async Task Development_WithNoAllowedOriginsConfigured_AllowsAnyOrigin()
    {
        // Default test-host environment is Development (WebApplicationFactory's own
        // default) -- no Cors:AllowedOrigins override, matching every other test in this
        // project that relies on this same default.
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Database", database.ConnectionString);
            builder.UseSetting("Authentication:Jwt:SigningKey", SigningKey);
            builder.UseSetting("ServiceAuth:Jwt:SigningKeyPem", SigningKeyPem);
        });

        var allowOrigin = await AllowOriginHeaderAsync(factory, "https://any-dev-frontend.example.com");

        // AllowAnyOrigin() sets the literal wildcard, not an echo of the request's Origin.
        Assert.Equal("*", allowOrigin);
    }

    [Fact]
    public async Task Production_WithNoAllowedOriginsConfigured_DeniesEveryOrigin()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Database", database.ConnectionString);
            builder.UseSetting("Authentication:Jwt:SigningKey", SigningKey);
            builder.UseSetting("ServiceAuth:Jwt:SigningKeyPem", SigningKeyPem);
            // "localhost", not some other placeholder: Stage 15's guard only requires this
            // not be null/"*" in Production, but TestServer's default HttpClient sends
            // Host: localhost -- a mismatched value here would make ASP.NET Core's own
            // host-filtering middleware reject every request with 400 before CORS ever
            // runs, which would make this test pass for the wrong reason (a host-filtering
            // rejection looks identical to a CORS denial from the client's point of view).
            builder.UseSetting("AllowedHosts", "localhost");
        });

        var allowOrigin = await AllowOriginHeaderAsync(factory, "https://some-frontend.example.com");

        Assert.Null(allowOrigin); // deny by default -- no Access-Control-Allow-Origin at all.
    }

    [Fact]
    public async Task Production_WithAnExplicitlyConfiguredOrigin_AllowsOnlyThatOrigin()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Database", database.ConnectionString);
            builder.UseSetting("Authentication:Jwt:SigningKey", SigningKey);
            builder.UseSetting("ServiceAuth:Jwt:SigningKeyPem", SigningKeyPem);
            builder.UseSetting("AllowedHosts", "localhost");
            builder.UseSetting("Cors:AllowedOrigins:0", "https://approved-frontend.example.com");
        });

        var allowedOrigin = await AllowOriginHeaderAsync(factory, "https://approved-frontend.example.com");
        var deniedOrigin = await AllowOriginHeaderAsync(factory, "https://not-on-the-list.example.com");

        Assert.Equal("https://approved-frontend.example.com", allowedOrigin);
        Assert.Null(deniedOrigin);
    }

    [Fact]
    public async Task Development_WithAnExplicitlyConfiguredOrigin_StillDeniesOthers()
    {
        // An explicit list always wins, even in Development -- a developer rehearsing
        // Production behavior locally is not silently overridden back to permissive.
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Database", database.ConnectionString);
            builder.UseSetting("Authentication:Jwt:SigningKey", SigningKey);
            builder.UseSetting("ServiceAuth:Jwt:SigningKeyPem", SigningKeyPem);
            builder.UseSetting("Cors:AllowedOrigins:0", "https://approved-frontend.example.com");
        });

        var deniedOrigin = await AllowOriginHeaderAsync(factory, "https://any-dev-frontend.example.com");

        Assert.Null(deniedOrigin);
    }
}
