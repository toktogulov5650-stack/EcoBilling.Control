using System.Net;
using System.Net.Http.Json;
using EcoBilling.Control.Api.Endpoints;
using EcoBilling.Control.Api.Endpoints.Public;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EcoBilling.Control.EndToEndTests;

/// <summary>
/// Exercises <c>POST /api/v1/districts/resolve</c> over real HTTP, against a real
/// PostgreSQL database (see <see cref="DatabaseFixture"/>).
/// </summary>
/// <remarks>
/// Every test uses its own, never-reused district code: the fixture's database is shared
/// across the whole test run (a fresh container per test would be needlessly slow), and
/// tests within one class already run sequentially by xUnit's default -- but a repeated
/// code would still collide with a row a previous test left behind, since nothing here
/// truncates the table between tests. Distinct codes side-step that without needing a
/// reset step.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class ResolveDistrictEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public ResolveDistrictEndpointTests(DatabaseFixture database)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Database", database.ConnectionString));
    }

    private async Task SeedAsync(District district)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        dbContext.Districts.Add(district);
        await dbContext.SaveChangesAsync();
    }

    private static District ActiveDistrict(string code, string apiBaseUrl)
    {
        var district = District.Create(
            DistrictId.New(),
            DistrictCode.Create(code).Value,
            "Test district",
            TrustedApiUrl.Create(apiBaseUrl).Value,
            DateTimeOffset.UtcNow).Value;

        district.Activate(DateTimeOffset.UtcNow);

        return district;
    }

    [Fact]
    public async Task Resolve_ReturnsTheAddress_ForAnActiveDistrict()
    {
        await SeedAsync(ActiveDistrict("BISHKEK-01", "https://district-01.example.com/api"));
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/districts/resolve",
            new ResolveDistrictRequest("BISHKEK-01"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ResolveDistrictResponse>();
        Assert.NotNull(body);
        Assert.Equal("BISHKEK-01", body.DistrictCode);
        Assert.Equal("https://district-01.example.com/api", body.ApiBaseUrl);
        Assert.Equal(3600, body.ExpiresInSeconds);
    }

    [Fact]
    public async Task Resolve_StripsTheRedundantRootSlash_ForABareHostDistrict()
    {
        // TrustedApiUrl correctly (and deliberately, per its Stage 1 tests) canonicalizes
        // a bare-host address to include a trailing "/". The wire response must not
        // repeat that slash, or a client appending its own path would get "host//path".
        await SeedAsync(ActiveDistrict("OSH-01", "https://district-02.example.com"));
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/districts/resolve",
            new ResolveDistrictRequest("OSH-01"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ResolveDistrictResponse>();
        Assert.NotNull(body);
        Assert.Equal("https://district-02.example.com", body.ApiBaseUrl);
    }

    [Fact]
    public async Task Resolve_NormalizesTheCodeBeforeLookup()
    {
        await SeedAsync(ActiveDistrict("BISHKEK-02", "https://district-03.example.com/api"));
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/districts/resolve",
            new ResolveDistrictRequest("  bishkek-02  "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_ReturnsNotFound_ForAnUnknownCode()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/districts/resolve",
            new ResolveDistrictRequest("UNKNOWN-99"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("district.not_found", body.Code);
        Assert.False(string.IsNullOrEmpty(body.TraceId));

        // Deliberately generic -- an unknown code has no district to reference, unlike
        // district.inactive below, which does get an actionable message (Q-follow-up).
        Assert.DoesNotContain("администратору", body.Message);
    }

    [Fact]
    public async Task Resolve_ReturnsNotFound_ForAKnownButInactiveDistrict()
    {
        var district = District.Create(
            DistrictId.New(),
            DistrictCode.Create("BISHKEK-03").Value,
            "Test district",
            TrustedApiUrl.Create("https://district-04.example.com/api").Value,
            DateTimeOffset.UtcNow).Value;
        await SeedAsync(district); // never activated
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/districts/resolve",
            new ResolveDistrictRequest("BISHKEK-03"));

        // Same HTTP status as an unknown code -- only the body's error code differs.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("district.inactive", body.Code);

        // Generic and actionable, but never the district's own name or any internal detail.
        Assert.Equal("Обратитесь к администратору вашего округа.", body.Message);
        Assert.DoesNotContain(district.Code, body.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a valid code")]
    public async Task Resolve_ReturnsBadRequest_ForAnInvalidCode(string? code)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/districts/resolve",
            new ResolveDistrictRequest(code));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("district.invalid_code", body.Code);
    }

    [Fact]
    public async Task Resolve_DoesNotExposeDistrictIdOrDistrictNameInTheResponse()
    {
        await SeedAsync(ActiveDistrict("BISHKEK-04", "https://district-05.example.com/api"));
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/districts/resolve",
            new ResolveDistrictRequest("BISHKEK-04"));

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("districtId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("districtName", raw, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => _factory.Dispose();
}
