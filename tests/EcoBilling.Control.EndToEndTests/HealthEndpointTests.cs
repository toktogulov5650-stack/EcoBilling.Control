using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EcoBilling.Control.EndToEndTests;

/// <summary>
/// Verifies <c>/health</c> (pure liveness -- no dependency checks) and <c>/ready</c>
/// (Stage 14: additionally proves PostgreSQL is reachable) are actually distinct.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class HealthEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _healthyFactory;

    public HealthEndpointTests(DatabaseFixture database)
    {
        _healthyFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Database", database.ConnectionString);
            builder.UseSetting("Authentication:Jwt:SigningKey", "e2e-test-signing-key-at-least-32-bytes-long-hmac");
        });
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var client = _healthyFactory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_ReturnsHealthy_WhenPostgresIsReachable()
    {
        var client = _healthyFactory.CreateClient();

        var response = await client.GetAsync("/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_ReturnsUnhealthy_WhenPostgresIsNotReachable()
    {
        // A deliberately broken connection string -- a port nothing listens on, not the
        // shared DatabaseFixture -- so this test proves /ready actually distinguishes
        // "process is up" from "its database connection works," rather than just
        // returning success unconditionally.
        using var unhealthyFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "ConnectionStrings:Database",
                "Host=127.0.0.1;Port=1;Database=nonexistent;Username=nonexistent;Password=nonexistent;Timeout=1;Command Timeout=1");
            builder.UseSetting("Authentication:Jwt:SigningKey", "e2e-test-signing-key-at-least-32-bytes-long-hmac");
        });
        var client = unhealthyFactory.CreateClient();

        var response = await client.GetAsync("/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    public void Dispose() => _healthyFactory.Dispose();
}
