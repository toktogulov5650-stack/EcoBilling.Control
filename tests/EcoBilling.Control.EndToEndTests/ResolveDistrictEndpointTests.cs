using System.Net;
using System.Net.Http.Json;
using EcoBilling.Control.Api.Endpoints;
using EcoBilling.Control.Api.Endpoints.Public;
using EcoBilling.Control.Api.Extensions;
using EcoBilling.Control.Domain.Districts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EcoBilling.Control.EndToEndTests;

/// <summary>
/// Exercises <c>POST /api/v1/districts/resolve</c> over real HTTP, against the temporary
/// in-memory repository (see <see cref="TemporaryInMemoryDistrictRepository"/>). Seeding
/// happens by resolving that repository from the test host's DI container directly,
/// because there is no CreateDistrict endpoint yet (Stage 5) to seed data through HTTP.
/// </summary>
public sealed class ResolveDistrictEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new();

    private void Seed(District district) =>
        _factory.Services.GetRequiredService<TemporaryInMemoryDistrictRepository>().Seed(district);

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
        Seed(ActiveDistrict("BISHKEK-01", "https://district-01.example.com/api"));
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
    public async Task Resolve_NormalizesTheCodeBeforeLookup()
    {
        Seed(ActiveDistrict("BISHKEK-01", "https://district-01.example.com/api"));
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/districts/resolve",
            new ResolveDistrictRequest("  bishkek-01  "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_ReturnsNotFound_ForAnUnknownCode()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/districts/resolve",
            new ResolveDistrictRequest("OSH-01"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("district.not_found", body.Code);
        Assert.False(string.IsNullOrEmpty(body.TraceId));
    }

    [Fact]
    public async Task Resolve_ReturnsNotFound_ForAKnownButInactiveDistrict()
    {
        var district = District.Create(
            DistrictId.New(),
            DistrictCode.Create("BISHKEK-01").Value,
            "Test district",
            TrustedApiUrl.Create("https://district-01.example.com/api").Value,
            DateTimeOffset.UtcNow).Value;
        Seed(district); // never activated
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/districts/resolve",
            new ResolveDistrictRequest("BISHKEK-01"));

        // Same HTTP status as an unknown code -- only the body's error code differs.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("district.inactive", body.Code);
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
        Seed(ActiveDistrict("BISHKEK-01", "https://district-01.example.com/api"));
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/districts/resolve",
            new ResolveDistrictRequest("BISHKEK-01"));

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("districtId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("districtName", raw, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => _factory.Dispose();
}
