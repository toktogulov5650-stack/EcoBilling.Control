using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EcoBilling.Control.Api.Endpoints.Administration;
using EcoBilling.Control.Api.Endpoints.Public;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EcoBilling.Control.EndToEndTests;

/// <summary>
/// The district admin endpoints Stage 5 built handlers for but never mapped to HTTP,
/// finally wired here (Stage 7) behind real authentication and a real audit trail.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DistrictAdministrationEndpointTests : IDisposable
{
    private const string KnownPassword = "a-known-correct-password-123";

    private readonly WebApplicationFactory<Program> _factory;

    public DistrictAdministrationEndpointTests(DatabaseFixture database)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Database", database.ConnectionString);
            builder.UseSetting("Authentication:Jwt:SigningKey", "e2e-test-signing-key-at-least-32-bytes-long-hmac");

            builder.UseSetting("Districts:AllowedHosts:0", "e2e-01.example.com");
            builder.UseSetting("Districts:AllowedHosts:1", "e2e-02.example.com");
            builder.UseSetting("Districts:AllowedHosts:2", "e2e-02b.example.com");
        });
    }

    private async Task<AdministratorId> SeedAdministratorAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var administrator = Administrator.Create(
            AdministratorId.New(),
            AdministratorEmail.Create(email).Value,
            "Test Administrator",
            passwordHasher.Hash(KnownPassword),
            DateTimeOffset.UtcNow).Value;

        dbContext.Administrators.Add(administrator);
        await dbContext.SaveChangesAsync();

        return administrator.Id;
    }

    private static async Task<string> LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/login", new AdministratorLoginRequest(email, KnownPassword));
        var session = await response.Content.ReadFromJsonAsync<AdministratorSessionResponse>();

        return session!.AccessToken;
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string accessToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    [Fact]
    public async Task FullLifecycle_CreateListGetUpdateActivateDeactivate_AllSucceedAndAreAudited()
    {
        var administratorId = await SeedAdministratorAsync("district-admin@example.com");
        var client = _factory.CreateClient();
        var accessToken = await LoginAsync(client, "district-admin@example.com");

        // Create.
        var createResponse = await client.SendAsync(Authorized(
            HttpMethod.Post, "/api/v1/admin/districts", accessToken,
            new CreateDistrictRequest("EEND-01", "E2E district", "https://e2e-01.example.com/api")));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateDistrictResponse>();
        Assert.NotNull(created);

        // Get one.
        var getResponse = await client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/admin/districts/{created.DistrictId}", accessToken));
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<DistrictResponse>();
        Assert.NotNull(fetched);
        Assert.Equal("EEND-01", fetched.Code.ToUpperInvariant());
        Assert.Equal("Inactive", fetched.Status);

        // List includes it.
        var listResponse = await client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/admin/districts", accessToken));
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<ListDistrictsResponse>();
        Assert.NotNull(list);
        Assert.Contains(list.Items, d => d.DistrictId == created.DistrictId);

        // Update.
        var updateResponse = await client.SendAsync(Authorized(
            HttpMethod.Put, $"/api/v1/admin/districts/{created.DistrictId}", accessToken,
            new UpdateDistrictRequest("Renamed E2E district", null)));
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        // Activate.
        var activateResponse = await client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/districts/{created.DistrictId}/activate", accessToken));
        Assert.Equal(HttpStatusCode.NoContent, activateResponse.StatusCode);

        // Deactivate.
        var deactivateResponse = await client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/districts/{created.DistrictId}/deactivate", accessToken));
        Assert.Equal(HttpStatusCode.NoContent, deactivateResponse.StatusCode);

        // The whole lifecycle shows up in the audit trail, attributed to the calling administrator.
        var auditResponse = await client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/admin/audit?pageSize=50", accessToken));
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);
        var audit = await auditResponse.Content.ReadFromJsonAsync<AuditEntriesResponse>();
        Assert.NotNull(audit);

        var entriesForThisDistrict = audit.Items
            .Where(e => e.EntityId == created.DistrictId && e.AdministratorId == administratorId.Value)
            .Select(e => e.Action)
            .ToList();

        Assert.Contains("district.created", entriesForThisDistrict);
        Assert.Contains("district.updated", entriesForThisDistrict);
        Assert.Contains("district.activated", entriesForThisDistrict);
        Assert.Contains("district.deactivated", entriesForThisDistrict);
    }

    [Theory]
    [InlineData("POST", "/api/v1/admin/districts")]
    [InlineData("GET", "/api/v1/admin/districts")]
    [InlineData("GET", "/api/v1/admin/audit")]
    public async Task AdminEndpoints_RejectRequestsWithNoAccessToken(string method, string url)
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateDistrict_RejectsAConflictingCode_AndAuditsTheFailure()
    {
        var administratorId = await SeedAdministratorAsync("conflict-admin@example.com");
        var client = _factory.CreateClient();
        var accessToken = await LoginAsync(client, "conflict-admin@example.com");

        var firstResponse = await client.SendAsync(Authorized(
            HttpMethod.Post, "/api/v1/admin/districts", accessToken,
            new CreateDistrictRequest("EEND-02", "First", "https://e2e-02.example.com/api")));
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var secondResponse = await client.SendAsync(Authorized(
            HttpMethod.Post, "/api/v1/admin/districts", accessToken,
            new CreateDistrictRequest("eend-02", "Second", "https://e2e-02b.example.com/api")));

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        var auditResponse = await client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/admin/audit?pageSize=50", accessToken));
        var audit = await auditResponse.Content.ReadFromJsonAsync<AuditEntriesResponse>();
        Assert.NotNull(audit);
        Assert.Contains(audit.Items, e => e.Action == "district.create_failed" && e.AdministratorId == administratorId.Value);
    }

    [Fact]
    public async Task ResolveDistrict_IsActuallyCached_NotJustConsistentByCoincidence()
    {
        // Deliberately bypasses UpdateDistrict/DeactivateDistrict's own cache
        // invalidation (Stage 12) by deactivating directly through the DbContext, the
        // same way a test would reach around any handler to inspect raw state. If
        // ResolveDistrict were hitting PostgreSQL on every call, this deactivation would
        // be visible immediately; if it returns the stale, still-active address instead,
        // that is direct proof the cache -- not just consistent responses by luck -- is
        // what's actually being served.
        var (district, _) = await CreateAndActivateDistrictAsync("cached-bypass-admin@example.com", "EEND-03", "https://e2e-01.example.com/api");
        var client = _factory.CreateClient();

        var firstResolve = await client.PostAsJsonAsync("/api/v1/districts/resolve", new ResolveDistrictRequest("EEND-03"));
        Assert.Equal(HttpStatusCode.OK, firstResolve.StatusCode); // primes the cache.

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var districtId = new DistrictId(Guid.Parse(district.DistrictId));
            var tracked = await dbContext.Districts.SingleAsync(d => d.Id == districtId);
            tracked.Deactivate(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(); // no handler involved -- nothing invalidates the cache.
        }

        var secondResolve = await client.PostAsJsonAsync("/api/v1/districts/resolve", new ResolveDistrictRequest("EEND-03"));

        Assert.Equal(HttpStatusCode.OK, secondResolve.StatusCode); // still the stale, cached success -- not district.inactive.
    }

    [Fact]
    public async Task UpdateDistrict_InvalidatesTheResolveCache_SoTheNewAddressIsVisibleImmediately()
    {
        var (district, accessToken) = await CreateAndActivateDistrictAsync(
            "cache-invalidation-admin@example.com", "EEND-04", "https://e2e-01.example.com/api");
        var client = _factory.CreateClient();

        var firstResolve = await client.PostAsJsonAsync("/api/v1/districts/resolve", new ResolveDistrictRequest("EEND-04"));
        var firstResult = await firstResolve.Content.ReadFromJsonAsync<ResolveDistrictResponse>();
        Assert.NotNull(firstResult);
        Assert.Equal("https://e2e-01.example.com/api", firstResult.ApiBaseUrl); // primes the cache.

        var updateResponse = await client.SendAsync(Authorized(
            HttpMethod.Put, $"/api/v1/admin/districts/{district.DistrictId}", accessToken,
            new UpdateDistrictRequest(null, "https://e2e-02.example.com/api")));
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode); // invalidates the cache (Stage 12).

        var secondResolve = await client.PostAsJsonAsync("/api/v1/districts/resolve", new ResolveDistrictRequest("EEND-04"));
        var secondResult = await secondResolve.Content.ReadFromJsonAsync<ResolveDistrictResponse>();

        Assert.NotNull(secondResult);
        Assert.Equal("https://e2e-02.example.com/api", secondResult.ApiBaseUrl); // the new address, immediately -- not the 300s-old cached one.
    }

    private async Task<(CreateDistrictResponse District, string AccessToken)> CreateAndActivateDistrictAsync(
        string adminEmail, string code, string apiBaseUrl)
    {
        await SeedAdministratorAsync(adminEmail);
        var client = _factory.CreateClient();
        var accessToken = await LoginAsync(client, adminEmail);

        var createResponse = await client.SendAsync(Authorized(
            HttpMethod.Post, "/api/v1/admin/districts", accessToken,
            new CreateDistrictRequest(code, "Cache test district", apiBaseUrl)));
        var created = (await createResponse.Content.ReadFromJsonAsync<CreateDistrictResponse>())!;

        await client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/districts/{created.DistrictId}/activate", accessToken));

        return (created, accessToken);
    }

    public void Dispose() => _factory.Dispose();
}
