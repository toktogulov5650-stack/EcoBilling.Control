using EcoBilling.Control.Application.Districts.ResolveDistrict;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.UnitTests.TestDoubles;

namespace EcoBilling.Control.UnitTests.Districts.ResolveDistrict;

public sealed class ResolveDistrictHandlerTests
{
    private static District ActiveDistrict(
        string code = "BISHKEK-01",
        string apiBaseUrl = "https://district-01.example.com/api")
    {
        var district = District.Create(
            DistrictId.New(),
            DistrictCode.Create(code).Value,
            "Bishkek district",
            TrustedApiUrl.Create(apiBaseUrl).Value,
            DateTimeOffset.UtcNow).Value;

        district.Activate(DateTimeOffset.UtcNow);

        return district;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-code")]
    [InlineData("AB")]
    public async Task HandleAsync_RejectsAMalformedCodeWithoutQueryingTheRepository(string? input)
    {
        var repository = new FakeDistrictRepository();
        var handler = new ResolveDistrictHandler(repository, new FakeCache());

        var result = await handler.HandleAsync(new ResolveDistrictQuery(input), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.invalid_code", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_ReturnsNotFoundForAnUnknownCode()
    {
        var repository = new FakeDistrictRepository();
        var handler = new ResolveDistrictHandler(repository, new FakeCache());

        var result = await handler.HandleAsync(new ResolveDistrictQuery("OSH-01"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_ReturnsInactiveForAKnownButInactiveDistrict()
    {
        var repository = new FakeDistrictRepository();
        var district = District.Create(
            DistrictId.New(),
            DistrictCode.Create("BISHKEK-01").Value,
            "Bishkek district",
            TrustedApiUrl.Create("https://district-01.example.com").Value,
            DateTimeOffset.UtcNow).Value;
        repository.Seed(district);
        var handler = new ResolveDistrictHandler(repository, new FakeCache());

        var result = await handler.HandleAsync(new ResolveDistrictQuery("BISHKEK-01"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.inactive", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_DoesNotDistinguishNotFoundFromInactiveByAnythingOtherThanTheErrorCode()
    {
        // Both failures must be equally uninformative about which case occurred, except
        // for the error code itself -- this is what keeps the endpoint from leaking
        // whether a district technically exists (brief, section 9).
        var repository = new FakeDistrictRepository();
        var inactiveDistrict = District.Create(
            DistrictId.New(),
            DistrictCode.Create("BISHKEK-01").Value,
            "Bishkek district",
            TrustedApiUrl.Create("https://district-01.example.com").Value,
            DateTimeOffset.UtcNow).Value;
        repository.Seed(inactiveDistrict);
        var handler = new ResolveDistrictHandler(repository, new FakeCache());

        var notFoundResult = await handler.HandleAsync(new ResolveDistrictQuery("OSH-01"), CancellationToken.None);
        var inactiveResult = await handler.HandleAsync(new ResolveDistrictQuery("BISHKEK-01"), CancellationToken.None);

        Assert.True(notFoundResult.IsFailure);
        Assert.True(inactiveResult.IsFailure);
        Assert.NotEqual(notFoundResult.Error.Code, inactiveResult.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_ReturnsTheAddressForAnActiveDistrict()
    {
        var repository = new FakeDistrictRepository();
        var district = ActiveDistrict();
        repository.Seed(district);
        var handler = new ResolveDistrictHandler(repository, new FakeCache());

        var result = await handler.HandleAsync(new ResolveDistrictQuery("BISHKEK-01"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("BISHKEK-01", result.Value.DistrictCode);
        Assert.Equal("https://district-01.example.com/api", result.Value.ApiBaseUrl);
        Assert.Equal(ResolveDistrictHandler.ExpiresInSeconds, result.Value.ExpiresInSeconds);
    }

    [Fact]
    public async Task HandleAsync_ReturnsTheCanonicalCodeEvenIfTheClientSentLowercase()
    {
        var repository = new FakeDistrictRepository();
        var district = ActiveDistrict();
        repository.Seed(district);
        var handler = new ResolveDistrictHandler(repository, new FakeCache());

        var result = await handler.HandleAsync(new ResolveDistrictQuery("bishkek-01"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("BISHKEK-01", result.Value.DistrictCode);
    }

    [Fact]
    public async Task HandleAsync_OnASuccessfulResolve_PopulatesTheCache()
    {
        var repository = new FakeDistrictRepository();
        repository.Seed(ActiveDistrict());
        var cache = new FakeCache();
        var handler = new ResolveDistrictHandler(repository, cache);

        await handler.HandleAsync(new ResolveDistrictQuery("BISHKEK-01"), CancellationToken.None);

        var cached = await cache.GetAsync<ResolveDistrictResult>("district:resolve:BISHKEK-01", CancellationToken.None);
        Assert.NotNull(cached);
        Assert.Equal("BISHKEK-01", cached.DistrictCode);
    }

    [Fact]
    public async Task HandleAsync_OnACacheHit_NeverQueriesTheRepository()
    {
        var repository = new FakeDistrictRepository();
        var cache = new FakeCache();
        cache.Seed("district:resolve:BISHKEK-01", new ResolveDistrictResult("BISHKEK-01", "https://cached.example.com/api", 3600));
        var handler = new ResolveDistrictHandler(repository, cache);

        var result = await handler.HandleAsync(new ResolveDistrictQuery("BISHKEK-01"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://cached.example.com/api", result.Value.ApiBaseUrl);
        Assert.Empty(repository.Added); // sanity: nothing was ever seeded into the repository either.
    }

    [Theory]
    [InlineData("OSH-01")] // not_found
    [InlineData("BISHKEK-01")] // inactive
    public async Task HandleAsync_NeverCachesANegativeResult(string code)
    {
        var repository = new FakeDistrictRepository();
        var inactiveDistrict = District.Create(
            DistrictId.New(),
            DistrictCode.Create("BISHKEK-01").Value,
            "Bishkek district",
            TrustedApiUrl.Create("https://district-01.example.com").Value,
            DateTimeOffset.UtcNow).Value; // never activated
        repository.Seed(inactiveDistrict);
        var cache = new FakeCache();
        var handler = new ResolveDistrictHandler(repository, cache);

        await handler.HandleAsync(new ResolveDistrictQuery(code), CancellationToken.None);

        var cached = await cache.GetAsync<ResolveDistrictResult>(
            $"district:resolve:{code}", CancellationToken.None);
        Assert.Null(cached);
    }

    [Fact]
    public async Task HandleAsync_NeverExposesDistrictIdOrDistrictName()
    {
        // ResolveDistrictResult's shape is the guarantee here: it has no property that
        // could carry an internal identifier or registry name to the client.
        var properties = typeof(ResolveDistrictResult).GetProperties().Select(p => p.Name);

        Assert.Equal(
            new[] { "DistrictCode", "ApiBaseUrl", "ExpiresInSeconds" },
            properties);
    }
}
