using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EcoBilling.Control.Api.Endpoints.Administration;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EcoBilling.Control.EndToEndTests;

/// <summary>Exercises the full administrator session lifecycle over real HTTP, against a real PostgreSQL database.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class AdministratorAuthEndpointTests : IDisposable
{
    private const string KnownPassword = "a-known-correct-password-123";

    private readonly WebApplicationFactory<Program> _factory;

    public AdministratorAuthEndpointTests(DatabaseFixture database)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Database", database.ConnectionString);
            // Explicit, not relying on appsettings.Development.json loading implicitly:
            // this is the one value every test in this class actually depends on.
            builder.UseSetting("Authentication:Jwt:SigningKey", "e2e-test-signing-key-at-least-32-bytes-long-hmac");
        });
    }

    private async Task<Administrator> SeedAdministratorAsync(string email)
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

        return administrator;
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return request;
    }

    [Fact]
    public async Task FullSessionLifecycle_LoginWhoAmIRefreshLogout_WorksAsExpected()
    {
        await SeedAdministratorAsync("lifecycle@example.com");
        var client = _factory.CreateClient();

        // whoami without a token is rejected.
        var unauthorized = await client.GetAsync("/api/v1/admin/whoami");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        // Login.
        var loginResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/login",
            new AdministratorLoginRequest("lifecycle@example.com", KnownPassword));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var session = await loginResponse.Content.ReadFromJsonAsync<AdministratorSessionResponse>();
        Assert.NotNull(session);

        // whoami with the access token succeeds and identifies the right administrator.
        var whoAmIResponse = await client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/admin/whoami", session.AccessToken));
        Assert.Equal(HttpStatusCode.OK, whoAmIResponse.StatusCode);
        var identity = await whoAmIResponse.Content.ReadFromJsonAsync<WhoAmIResponse>();
        Assert.NotNull(identity);
        Assert.Equal("lifecycle@example.com", identity.Email);

        // Refresh rotates both tokens.
        var refreshResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/refresh",
            new RefreshAdministratorSessionRequest(session.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var rotated = await refreshResponse.Content.ReadFromJsonAsync<AdministratorSessionResponse>();
        Assert.NotNull(rotated);
        // Not asserting the access tokens differ: a JWT's exp/iat/nbf claims are
        // whole-second Unix timestamps, so two tokens for the same administrator
        // issued within the same wall-clock second are legitimately byte-identical --
        // this is not a security property anything relies on. The refresh token,
        // generated from fresh randomness on every issuance, is the one that must differ.
        Assert.NotEqual(session.RefreshToken, rotated.RefreshToken);

        // The new access token works too.
        var whoAmIAfterRefresh = await client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/admin/whoami", rotated.AccessToken));
        Assert.Equal(HttpStatusCode.OK, whoAmIAfterRefresh.StatusCode);

        // Logout revokes the current refresh token.
        var logoutResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/logout",
            new LogoutAdministratorSessionRequest(rotated.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        // The now-revoked refresh token can no longer be used.
        var refreshAfterLogout = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/refresh",
            new RefreshAdministratorSessionRequest(rotated.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);
    }

    [Fact]
    public async Task ReplayingARotatedOutRefreshToken_RevokesTheSessionThatReplacedItToo()
    {
        await SeedAdministratorAsync("reuse-detection@example.com");
        var client = _factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/login",
            new AdministratorLoginRequest("reuse-detection@example.com", KnownPassword));
        var original = await loginResponse.Content.ReadFromJsonAsync<AdministratorSessionResponse>();
        Assert.NotNull(original);

        // A legitimate rotation.
        var refreshResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/refresh",
            new RefreshAdministratorSessionRequest(original.RefreshToken));
        var rotated = await refreshResponse.Content.ReadFromJsonAsync<AdministratorSessionResponse>();
        Assert.NotNull(rotated);

        // Replaying the original, already-rotated-out token is rejected.
        var replayResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/refresh",
            new RefreshAdministratorSessionRequest(original.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);

        // The legitimately rotated session, which had done nothing wrong, is also now
        // revoked, as a defensive response to the suspected theft.
        var refreshRotatedAfterReplay = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/refresh",
            new RefreshAdministratorSessionRequest(rotated.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refreshRotatedAfterReplay.StatusCode);
    }

    [Fact]
    public async Task Login_ReturnsTheSameGenericFailure_ForAnUnknownEmailAndAWrongPassword()
    {
        await SeedAdministratorAsync("known@example.com");
        var client = _factory.CreateClient();

        var unknownEmailResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/login",
            new AdministratorLoginRequest("nobody@example.com", "anything-at-all"));
        var wrongPasswordResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/login",
            new AdministratorLoginRequest("known@example.com", "the-wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmailResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPasswordResponse.StatusCode);

        var unknownBody = await unknownEmailResponse.Content.ReadAsStringAsync();
        var wrongPasswordBody = await wrongPasswordResponse.Content.ReadAsStringAsync();
        Assert.Contains("administrator.invalid_credentials", unknownBody);
        Assert.Contains("administrator.invalid_credentials", wrongPasswordBody);
    }

    [Fact]
    public async Task Login_AfterFiveConsecutiveFailures_LocksTheAccount_EvenAgainstTheCorrectPasswordAfterward()
    {
        await SeedAdministratorAsync("lockout-e2e@example.com");
        var client = _factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/admin/auth/login",
                new AdministratorLoginRequest("lockout-e2e@example.com", "wrong-password"));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // Even the correct password is rejected while locked out (Stage 10) -- the
        // exact same generic response as every other failure case, never revealing that
        // a lockout, specifically, is why this attempt failed.
        var correctPasswordResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/auth/login",
            new AdministratorLoginRequest("lockout-e2e@example.com", KnownPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, correctPasswordResponse.StatusCode);
        var body = await correctPasswordResponse.Content.ReadAsStringAsync();
        Assert.Contains("administrator.invalid_credentials", body);
    }

    public void Dispose() => _factory.Dispose();
}
