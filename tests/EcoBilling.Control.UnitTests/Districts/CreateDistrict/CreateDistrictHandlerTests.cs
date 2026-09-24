using EcoBilling.Control.Application.Districts.CreateDistrict;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.UnitTests.TestDoubles;

namespace EcoBilling.Control.UnitTests.Districts.CreateDistrict;

public sealed class CreateDistrictHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(
        FakeDistrictRepository Repository,
        FakeUnitOfWork UnitOfWork,
        CreateDistrictHandler Handler);

    private static Fixture NewFixture(params string[] allowedHosts)
    {
        var repository = new FakeDistrictRepository();
        var unitOfWork = new FakeUnitOfWork();
        var allowlist = new FakeDistrictHostAllowlist(allowedHosts);
        var handler = new CreateDistrictHandler(repository, allowlist, unitOfWork, new FixedClock(Now));

        return new Fixture(repository, unitOfWork, handler);
    }

    [Fact]
    public async Task HandleAsync_RejectsAMalformedCode_WithoutTouchingTheRepositoryOrAllowlist()
    {
        var (repository, unitOfWork, handler) = NewFixture("district-01.example.com");

        var result = await handler.HandleAsync(
            new CreateDistrictCommand("not a code", "A district", "https://district-01.example.com"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.invalid_code", result.Error.Code);
        Assert.Empty(repository.Added);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_RejectsAStructurallyInvalidUrl()
    {
        var (_, _, handler) = NewFixture("district-01.example.com");

        var result = await handler.HandleAsync(
            new CreateDistrictCommand("BISHKEK-01", "A district", "http://district-01.example.com"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.invalid_url", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_RejectsAHostNotOnTheAllowlist_BeforeCheckingForACodeConflict()
    {
        var (repository, unitOfWork, handler) = NewFixture("district-01.example.com");

        var result = await handler.HandleAsync(
            new CreateDistrictCommand("BISHKEK-01", "A district", "https://not-allowed.example.com"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.host_not_allowed", result.Error.Code);
        Assert.Empty(repository.Added);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_RejectsACodeThatAlreadyExists()
    {
        var (repository, unitOfWork, handler) = NewFixture("district-01.example.com");
        repository.Seed(District.Create(
            DistrictId.New(),
            DistrictCode.Create("BISHKEK-01").Value,
            "Existing district",
            TrustedApiUrl.Create("https://district-01.example.com").Value,
            Now).Value);

        var result = await handler.HandleAsync(
            new CreateDistrictCommand("bishkek-01", "Another district", "https://district-01.example.com"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.code_conflict", result.Error.Code);
        Assert.Empty(repository.Added);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_RejectsAnEmptyName()
    {
        var (_, _, handler) = NewFixture("district-01.example.com");

        var result = await handler.HandleAsync(
            new CreateDistrictCommand("BISHKEK-01", "   ", "https://district-01.example.com"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.invalid_name", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_CreatesAnInactiveDistrict_AndCommitsExactlyOnce()
    {
        var (repository, unitOfWork, handler) = NewFixture("district-01.example.com");

        var result = await handler.HandleAsync(
            new CreateDistrictCommand("bishkek-01", "Bishkek district", "https://district-01.example.com"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("BISHKEK-01", result.Value.NormalizedCode);
        Assert.Single(repository.Added);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        var created = repository.Added[0];
        Assert.Equal(result.Value.DistrictId, created.Id);
        Assert.False(created.IsActive); // starts Inactive (Domain, Stage 1) -- CreateDistrict never activates on its own.
        Assert.Equal(Now, created.CreatedAt);
    }
}
