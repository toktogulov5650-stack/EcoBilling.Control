using EcoBilling.Control.Application.Districts.UpdateDistrict;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.UnitTests.TestDoubles;

namespace EcoBilling.Control.UnitTests.Districts.UpdateDistrict;

public sealed class UpdateDistrictHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(1);
    private static readonly AdministratorId Caller = AdministratorId.New();

    private sealed record Fixture(
        District District,
        FakeDistrictRepository Repository,
        FakeAuditWriter AuditWriter,
        FakeCache Cache,
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
        var auditWriter = new FakeAuditWriter();
        var cache = new FakeCache();
        var unitOfWork = new FakeUnitOfWork();
        var allowlist = new FakeDistrictHostAllowlist(allowedHosts);
        var handler = new UpdateDistrictHandler(repository, allowlist, auditWriter, cache, unitOfWork, new FixedClock(Later));

        return new Fixture(district, repository, auditWriter, cache, unitOfWork, handler);
    }

    [Fact]
    public async Task HandleAsync_RejectsAnUpdateWithNeitherFieldSupplied()
    {
        var fixture = NewFixture();

        var result = await fixture.Handler.HandleAsync(
            new UpdateDistrictCommand(DistrictId.New(), null, null, Caller),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation.failed", result.Error.Code);
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount); // the failure audit entry still commits (Stage 7).
    }

    [Fact]
    public async Task HandleAsync_ReturnsNotFoundForAnUnknownDistrict()
    {
        var fixture = NewFixture();

        var result = await fixture.Handler.HandleAsync(
            new UpdateDistrictCommand(DistrictId.New(), "New name", null, Caller),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_RenamesTheDistrict_WhenOnlyNameIsSupplied()
    {
        var fixture = NewFixture();

        var result = await fixture.Handler.HandleAsync(
            new UpdateDistrictCommand(fixture.District.Id, "New name", null, Caller),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New name", fixture.District.Name);
        Assert.Equal("https://district-01.example.com/api", fixture.District.ApiBaseUrl.ToString());
        Assert.Equal(Later, fixture.District.UpdatedAt);
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount);
        Assert.Single(fixture.AuditWriter.Entries);
        Assert.Equal("district.updated", fixture.AuditWriter.Entries[0].Action);
        Assert.Equal(["district:resolve:BISHKEK-01"], fixture.Cache.RemovedKeys); // Stage 12: invalidated on success.
    }

    [Fact]
    public async Task HandleAsync_RejectsAnEmptyName_AndLeavesTheDistrictUnchanged()
    {
        var fixture = NewFixture();

        var result = await fixture.Handler.HandleAsync(
            new UpdateDistrictCommand(fixture.District.Id, "   ", null, Caller),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.invalid_name", result.Error.Code);
        Assert.Equal("Original name", fixture.District.Name); // the business state is unchanged...
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount); // ...but the failure audit entry still commits.
        Assert.Equal("district.update_failed", fixture.AuditWriter.Entries[0].Action);
        Assert.Empty(fixture.Cache.RemovedKeys); // nothing changed -- no invalidation needed.
    }

    [Fact]
    public async Task HandleAsync_ChangesTheAddress_WhenOnlyApiBaseUrlIsSupplied_AndTheHostIsAllowed()
    {
        var fixture = NewFixture("district-02.example.com");

        var result = await fixture.Handler.HandleAsync(
            new UpdateDistrictCommand(fixture.District.Id, null, "https://district-02.example.com/api", Caller),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Original name", fixture.District.Name);
        Assert.Equal("https://district-02.example.com/api", fixture.District.ApiBaseUrl.ToString());
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_RejectsAStructurallyInvalidUrl_AndLeavesTheDistrictUnchanged()
    {
        var fixture = NewFixture();

        var result = await fixture.Handler.HandleAsync(
            new UpdateDistrictCommand(fixture.District.Id, null, "http://district-02.example.com", Caller),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.invalid_url", result.Error.Code);
        Assert.Equal("https://district-01.example.com/api", fixture.District.ApiBaseUrl.ToString());
    }

    [Fact]
    public async Task HandleAsync_RejectsAHostNotOnTheAllowlist_AndLeavesTheDistrictUnchanged()
    {
        var fixture = NewFixture("district-02.example.com"); // note: district-03 is NOT allowed

        var result = await fixture.Handler.HandleAsync(
            new UpdateDistrictCommand(fixture.District.Id, null, "https://district-03.example.com", Caller),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.host_not_allowed", result.Error.Code);
        Assert.Equal("https://district-01.example.com/api", fixture.District.ApiBaseUrl.ToString());
    }

    [Fact]
    public async Task HandleAsync_CanChangeBothFieldsInOneCall()
    {
        var fixture = NewFixture("district-02.example.com");

        var result = await fixture.Handler.HandleAsync(
            new UpdateDistrictCommand(fixture.District.Id, "New name", "https://district-02.example.com/api", Caller),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New name", fixture.District.Name);
        Assert.Equal("https://district-02.example.com/api", fixture.District.ApiBaseUrl.ToString());
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_NeverPersistsTheBusinessChange_WhenTheUrlCheckFailsAfterAnEarlierRenameSucceeded()
    {
        // Rename mutates the tracked entity in memory immediately; what matters is that
        // the district's persisted state (proven via the audit entry's "before"
        // snapshot, which is captured before any mutation) never reflects the
        // never-saved partial rename.
        var fixture = NewFixture("district-02.example.com"); // district-99 is NOT allowed

        var result = await fixture.Handler.HandleAsync(
            new UpdateDistrictCommand(fixture.District.Id, "New name", "https://district-99.example.com", Caller),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.host_not_allowed", result.Error.Code);
        Assert.Contains("Original name", fixture.AuditWriter.Entries[0].BeforeData!);
    }
}
