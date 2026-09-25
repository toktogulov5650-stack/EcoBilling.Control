using System.Net;
using System.Net.Http.Json;
using EcoBilling.Control.Api.Endpoints;
using EcoBilling.Control.Api.Endpoints.Administration;
using EcoBilling.Control.Api.Endpoints.Public;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EcoBilling.Control.EndToEndTests;

/// <summary>
/// Exercises the two rate-limiting policies (Stage 10) over real HTTP. Each test class
/// gets its own fresh <see cref="WebApplicationFactory{TEntryPoint}"/> per test method
/// (xUnit's default: a new test class instance per [Fact]), so each test's rate-limiter
/// state starts empty -- there is no risk of one test's request volume affecting another.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RateLimitingEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public RateLimitingEndpointTests(DatabaseFixture database)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Database", database.ConnectionString);
            builder.UseSetting("Authentication:Jwt:SigningKey", "e2e-test-signing-key-at-least-32-bytes-long-hmac");
        });
    }

    [Fact]
    public async Task AdminLogin_Allows20RequestsPerMinute_AndRejectsThe21stWith429()
    {
        var client = _factory.CreateClient();

        for (var i = 0; i < 20; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/admin/auth/login", new AdministratorLoginRequest("unknown@example.com", "anything"));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); // unknown email -- rejected by the handler, not the limiter.
        }

        var rejected = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/login", new AdministratorLoginRequest("unknown@example.com", "anything"));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var body = await rejected.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("rate_limit.exceeded", body.Code);
    }

    [Fact]
    public async Task ResolveDistrict_Allows30RequestsPerMinute_AndRejectsThe31stWith429()
    {
        var client = _factory.CreateClient();

        for (var i = 0; i < 30; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/districts/resolve", new ResolveDistrictRequest("NEVER-SEEDED-99"));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode); // still within the window.
        }

        var rejected = await client.PostAsJsonAsync(
            "/api/v1/districts/resolve", new ResolveDistrictRequest("NEVER-SEEDED-99"));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var body = await rejected.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("rate_limit.exceeded", body.Code);
    }

    public void Dispose() => _factory.Dispose();
}
