using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EcoBilling.Control.Api.Endpoints.Administration;
using EcoBilling.Control.Api.Endpoints.Public;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EcoBilling.Control.EndToEndTests;

/// <summary>
/// Exercises CreateDirector / ResetDirectorPassword / GetProvisioningOperation (Stage 8)
/// over real HTTP and a real PostgreSQL database, with a fake <see cref="IDistrictClient"/>
/// standing in for a district's own EcoBilling instance. A real district server isn't an
/// option here: <see cref="TrustedApiUrl"/> (Stage 1's SSRF protection) rejects
/// loopback/localhost addresses outright, and there is no legitimate
/// <see cref="District.ApiBaseUrl"/> a locally-hosted fake server could use instead (see
/// <c>DistrictClientTests</c> for the same reasoning at the unit level). This test
/// therefore covers everything Control itself does for real -- admin auth, persistence,
/// audit trail, idempotency-key reuse -- up to the exact boundary where a real network
/// call to a district would happen.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProvisioningEndpointTests : IDisposable
{
    private const string KnownPassword = "a-known-correct-password-123";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly FakeDistrictClient _districtClient = new();

    public ProvisioningEndpointTests(DatabaseFixture database)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Database", database.ConnectionString);
            builder.UseSetting("Authentication:Jwt:SigningKey", "e2e-test-signing-key-at-least-32-bytes-long-hmac");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDistrictClient>();
                services.AddSingleton<IDistrictClient>(_districtClient);
            });
        });
    }

    private sealed class FakeDistrictClient : IDistrictClient
    {
        public Func<CreateDirectorRequest, Result<DirectorCreationAcknowledged>> CreateDirectorResponder { get; set; } =
            _ => Result.Success(new DirectorCreationAcknowledged("director-e2e-1"));

        public Func<string, Result> ResetDirectorPasswordResponder { get; set; } = _ => Result.Success();

        public int CreateDirectorCallCount { get; private set; }

        public Task<Result<DirectorCreationAcknowledged>> CreateDirectorAsync(
            District district, CreateDirectorRequest request, string idempotencyKey, CancellationToken cancellationToken)
        {
            CreateDirectorCallCount++;

            return Task.FromResult(CreateDirectorResponder(request));
        }

        public Task<Result> ResetDirectorPasswordAsync(
            District district, string directorEmail, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(ResetDirectorPasswordResponder(directorEmail));
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

    private async Task<District> SeedActiveDistrictAsync(string code)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        var district = District.Create(
            DistrictId.New(),
            DistrictCode.Create(code).Value,
            "Test district",
            TrustedApiUrl.Create("https://prov-01.example.com/api").Value,
            DateTimeOffset.UtcNow).Value;
        district.Activate(DateTimeOffset.UtcNow);

        dbContext.Districts.Add(district);
        await dbContext.SaveChangesAsync();

        return district;
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
    public async Task CreateDirector_OnAnActiveDistrict_Succeeds_AndTheOperationCompletes()
    {
        var administratorId = await SeedAdministratorAsync("create-director-admin@example.com");
        var district = await SeedActiveDistrictAsync("PROVISION-01");
        var client = _factory.CreateClient();
        var accessToken = await LoginAsync(client, "create-director-admin@example.com");

        var response = await client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/districts/{district.Id.Value}/directors", accessToken,
            new CreateDirectorHttpRequest("New Director", "director@prov-01.example.com")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreateDirectorHttpResponse>();
        Assert.NotNull(created);
        Assert.Equal("director-e2e-1", created.DirectorId);

        var getResponse = await client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/admin/operations/{created.OperationId}", accessToken));
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var operation = await getResponse.Content.ReadFromJsonAsync<ProvisioningOperationResponse>();
        Assert.NotNull(operation);
        Assert.Equal("Completed", operation.Status);
        Assert.Equal("DirectorCreation", operation.OperationType);

        var auditResponse = await client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/admin/audit?pageSize=50", accessToken));
        var audit = await auditResponse.Content.ReadFromJsonAsync<AuditEntriesResponse>();
        Assert.NotNull(audit);
        Assert.Contains(audit.Items, e =>
            e.Action == "provisioning.director_creation_succeeded" &&
            e.AdministratorId == administratorId.Value &&
            e.EntityId == created.OperationId);
    }

    [Fact]
    public async Task CreateDirector_OnAnInactiveDistrict_Rejects_AndNeverCallsTheDistrictClient()
    {
        var district = District.Create(
            DistrictId.New(), DistrictCode.Create("PROVISION-02").Value, "Inactive district",
            TrustedApiUrl.Create("https://prov-01.example.com/api").Value, DateTimeOffset.UtcNow).Value; // never activated
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            dbContext.Districts.Add(district);
            await dbContext.SaveChangesAsync();
        }

        await SeedAdministratorAsync("inactive-district-admin@example.com");
        var client = _factory.CreateClient();
        var accessToken = await LoginAsync(client, "inactive-district-admin@example.com");

        var response = await client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/districts/{district.Id.Value}/directors", accessToken,
            new CreateDirectorHttpRequest("New Director", "director@prov-01.example.com")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // district.inactive maps to 404, same as district.not_found.
        Assert.Equal(0, _districtClient.CreateDirectorCallCount);
    }

    [Fact]
    public async Task CreateDirector_CalledTwiceForTheSameDistrict_TheSecondCallIsRejectedAsAlreadyExists()
    {
        await SeedAdministratorAsync("repeat-admin@example.com");
        var district = await SeedActiveDistrictAsync("PROVISION-03");
        var client = _factory.CreateClient();
        var accessToken = await LoginAsync(client, "repeat-admin@example.com");

        var firstResponse = await client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/districts/{district.Id.Value}/directors", accessToken,
            new CreateDirectorHttpRequest("First Director", "director@prov-01.example.com")));
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var secondResponse = await client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/districts/{district.Id.Value}/directors", accessToken,
            new CreateDirectorHttpRequest("Second Director", "another@prov-01.example.com")));

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.Equal(1, _districtClient.CreateDirectorCallCount); // the second request never reached the district client.
    }

    [Fact]
    public async Task ResetDirectorPassword_OnAnActiveDistrict_Succeeds()
    {
        var administratorId = await SeedAdministratorAsync("reset-admin@example.com");
        var district = await SeedActiveDistrictAsync("PROVISION-04");
        var client = _factory.CreateClient();
        var accessToken = await LoginAsync(client, "reset-admin@example.com");

        var response = await client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/districts/{district.Id.Value}/directors/reset-password", accessToken,
            new ResetDirectorPasswordHttpRequest("director@prov-01.example.com")));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var auditResponse = await client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/admin/audit?pageSize=50", accessToken));
        var audit = await auditResponse.Content.ReadFromJsonAsync<AuditEntriesResponse>();
        Assert.NotNull(audit);
        Assert.Contains(audit.Items, e =>
            e.Action == "provisioning.password_reset_succeeded" && e.AdministratorId == administratorId.Value);
    }

    [Theory]
    [InlineData("POST", "/api/v1/admin/districts/00000000-0000-0000-0000-000000000001/directors")]
    [InlineData("POST", "/api/v1/admin/districts/00000000-0000-0000-0000-000000000001/directors/reset-password")]
    [InlineData("GET", "/api/v1/admin/operations/00000000-0000-0000-0000-000000000001")]
    public async Task ProvisioningEndpoints_RejectRequestsWithNoAccessToken(string method, string url)
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
