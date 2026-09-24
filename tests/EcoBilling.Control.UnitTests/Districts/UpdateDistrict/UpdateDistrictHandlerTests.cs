using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Districts.UpdateDistrict;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.UnitTests.TestDoubles;

namespace EcoBilling.Control.UnitTests.Districts.UpdateDistrict;

public sealed class UpdateDistrictHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(1);

    private sealed record Fixture(
        District District,
        FakeDistrictRepository Repository,
        FakeUnitOfWork UnitOfWork,
        UpdateDistrictHandler Handler);

    private static Fixture NewFixture(params string[] allowedHosts)
    {
        var district = District.Create(
            DistrictId.New(),
            DistrictCode.Create("BISHKEK-01").Value,
            "Original name",
            TrustedApiUrl.Create("https://district-01.example.com/api").Value,
            Now).Value;

        var repository = new FakeDistrictRepository();
        repository.Seed(district);
        var unitOfWork = new FakeUnitOfWork();
        var allowlist = new FakeDistrictHostAllowlist(allowedHosts);
        var handler = new UpdateDistrictHandler(repository, allowlist, unitOfWork, new FixedClock(Later));

        return new Fixture(district, repository, unitOfWork, handler);
    }

    [Fact]
    public async Task HandleAsync_RejectsAnUpdateWithNeitherFieldSupplied()
    {
        var (_, _, unitOfWork, handler) = NewFixture();

        var result = await handler.HandleAsync(
            new UpdateDistrictCommand(DistrictId.New(), null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation.failed", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_ReturnsNotFoundForAnUnknownDistrict()
    {
        var fixture = NewFixture();

        var result = await fixture.Handler.HandleAsync(
            new UpdateDistrictCommand(DistrictId.New(), "New name", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_RenamesTheDistrict_WhenOnlyNameIsSupplied()
    {
        var fixture = NewFixture();

        var result = await fixture.Handler.HandleAsync(
            new UpdateDistrictCommand(fixture.District.Id, "New name", null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New name", fixture.District.Name);
        Assert.Equal("https://district-01.example.com/api", fixture.District.ApiBaseUrl.ToString());
        Assert.Equal(Later, fixture.District.UpdatedAt);
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_RejectsAnEmptyName_AndDoesNotCommit()
    {
        var fixture = NewFixture();

        var result = await fixture.Handler.HandleAsync(
            new UpdateDistrictCommand(fixture.District.Id, "   ", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.invalid_name", result.Error.Code);
        Assert.Equal("Original name", fixture.District.Name);
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_ChangesTheAddress_WhenOnlyApiBaseUrlIsSupplied_AndTheHostIsAllowed()
    {
        var fixture = NewFixture("district-02.example.com");

        var result = await fixture.Handler.HandleAsync(
            new UpdateDistrictCommand(fixture.District.Id, null, "https://district-02.example.com/api"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Original name", fixture.District.Name);
        Assert.Equal("https://district-02.example.com/api", fixture.District.ApiBaseUrl.ToString());
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_RejectsAStructurallyInvalidUrl_AndDoesNotCommit()
    {
        var fixture = NewFixture();

        var result = await fixture.Handler.HandleAsync(
            new UpdateDistrictCommand(fixture.District.Id, null, "http://district-02.example.com"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.invalid_url", result.Error.Code);
        Assert.Equal("https://district-01.example.com/api", fixture.District.ApiBaseUrl.ToString());
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_RejectsAHostNotOnTheAllowlist_AndDoesNotCommit()
    {
        var fixture = NewFixture("district-02.example.com"); // note: district-03 is NOT allowed

        var result = await fixture.Handler.HandleAsync(
            new UpdateDistrictCommand(fixture.District.Id, null, "https://district-03.example.com"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.host_not_allowed", result.Error.Code);
        Assert.Equal("https://district-01.example.com/api", fixture.District.ApiBaseUrl.ToString());
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_CanChangeBothFieldsInOneCall()
    {
        var fixture = NewFixture("district-02.example.com");

        var result = await fixture.Handler.HandleAsync(
            new UpdateDistrictCommand(fixture.District.Id, "New name", "https://district-02.example.com/api"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New name", fixture.District.Name);
        Assert.Equal("https://district-02.example.com/api", fixture.District.ApiBaseUrl.ToString());
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_NeverCommits_WhenTheUrlCheckFailsAfterAnEarlierRenameSucceeded()
    {
        // Rename mutates the tracked entity in memory immediately; what actually matters
        // is that SaveChangesAsync -- the one call that would flush anything to a real
        // database -- is never reached. (The fake repository shares the same District
        // reference the handler mutated, so it isn't a reliable witness to persistence
        // either way; SaveChangesCallCount is.)
        var fixture = NewFixture("district-02.example.com"); // district-99 is NOT allowed

        var result = await fixture.Handler.HandleAsync(
            new UpdateDistrictCommand(fixture.District.Id, "New name", "https://district-99.example.com"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.host_not_allowed", result.Error.Code);
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCallCount);
    }
}
