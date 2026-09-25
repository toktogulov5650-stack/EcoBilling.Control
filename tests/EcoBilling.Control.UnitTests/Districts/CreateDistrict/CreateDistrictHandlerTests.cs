using EcoBilling.Control.Application.Districts.CreateDistrict;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.UnitTests.TestDoubles;

namespace EcoBilling.Control.UnitTests.Districts.CreateDistrict;

public sealed class CreateDistrictHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly AdministratorId Caller = AdministratorId.New();

    private sealed record Fixture(
        FakeDistrictRepository Repository,
        FakeAuditWriter AuditWriter,
        FakeUnitOfWork UnitOfWork,
        CreateDistrictHandler Handler);

    private static Fixture NewFixture(params string[] allowedHosts)
    {
        var repository = new FakeDistrictRepository();
        var auditWriter = new FakeAuditWriter();
        var unitOfWork = new FakeUnitOfWork();
        var allowlist = new FakeDistrictHostAllowlist(allowedHosts);
        var handler = new CreateDistrictHandler(repository, allowlist, auditWriter, unitOfWork, new FixedClock(Now));

        return new Fixture(repository, auditWriter, unitOfWork, handler);
    }

    [Fact]
    public async Task HandleAsync_RejectsAMalformedCode_WithoutTouchingTheRepositoryOrAllowlist()
    {
        var fixture = NewFixture("district-01.example.com");

        var result = await fixture.Handler.HandleAsync(
            new CreateDistrictCommand("not a code", "A district", "https://district-01.example.com", Caller),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.invalid_code", result.Error.Code);
        Assert.Empty(fixture.Repository.Added); // no district staged...
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount); // ...but the failure audit entry still commits (Stage 7).
    }

    [Fact]
    public async Task HandleAsync_RejectsAStructurallyInvalidUrl()
    {
        var fixture = NewFixture("district-01.example.com");

        var result = await fixture.Handler.HandleAsync(
            new CreateDistrictCommand("BISHKEK-01", "A district", "http://district-01.example.com", Caller),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.invalid_url", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_RejectsAHostNotOnTheAllowlist_BeforeCheckingForACodeConflict()
    {
        var fixture = NewFixture("district-01.example.com");

        var result = await fixture.Handler.HandleAsync(
            new CreateDistrictCommand("BISHKEK-01", "A district", "https://not-allowed.example.com", Caller),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.host_not_allowed", result.Error.Code);
        Assert.Empty(fixture.Repository.Added); // no district staged...
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount); // ...but the failure audit entry still commits (Stage 7).
    }

    [Fact]
    public async Task HandleAsync_RejectsACodeThatAlreadyExists()
    {
        var fixture = NewFixture("district-01.example.com");
        fixture.Repository.Seed(District.Create(
            DistrictId.New(),
            DistrictCode.Create("BISHKEK-01").Value,
            "Existing district",
            TrustedApiUrl.Create("https://district-01.example.com").Value,
            Now).Value);

        var result = await fixture.Handler.HandleAsync(
            new CreateDistrictCommand("bishkek-01", "Another district", "https://district-01.example.com", Caller),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.code_conflict", result.Error.Code);
        Assert.Empty(fixture.Repository.Added);
    }

    [Fact]
    public async Task HandleAsync_RejectsAnEmptyName()
    {
        var fixture = NewFixture("district-01.example.com");

        var result = await fixture.Handler.HandleAsync(
            new CreateDistrictCommand("BISHKEK-01", "   ", "https://district-01.example.com", Caller),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.invalid_name", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_CreatesAnInactiveDistrict_AndCommitsExactlyOnce()
    {
        var fixture = NewFixture("district-01.example.com");

        var result = await fixture.Handler.HandleAsync(
            new CreateDistrictCommand("bishkek-01", "Bishkek district", "https://district-01.example.com", Caller),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("BISHKEK-01", result.Value.NormalizedCode);
        Assert.Single(fixture.Repository.Added);
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount);

        var created = fixture.Repository.Added[0];
        Assert.Equal(result.Value.DistrictId, created.Id);
        Assert.False(created.IsActive); // starts Inactive (Domain, Stage 1) -- CreateDistrict never activates on its own.
        Assert.Equal(Now, created.CreatedAt);
    }

    [Fact]
    public async Task HandleAsync_AuditsEveryOutcome_SuccessAndEveryFailure()
    {
        var fixture = NewFixture("district-01.example.com");

        await fixture.Handler.HandleAsync(
            new CreateDistrictCommand("not a code", null, null, Caller), CancellationToken.None);
        await fixture.Handler.HandleAsync(
            new CreateDistrictCommand("BISHKEK-02", "A district", "https://district-01.example.com", Caller),
            CancellationToken.None);

        Assert.Equal(2, fixture.AuditWriter.Entries.Count);
        Assert.Equal("district.create_failed", fixture.AuditWriter.Entries[0].Action);
        Assert.Equal(Caller.Value, fixture.AuditWriter.Entries[0].AdministratorId);
        Assert.Equal("district.created", fixture.AuditWriter.Entries[1].Action);
        Assert.Equal(Caller.Value, fixture.AuditWriter.Entries[1].AdministratorId);
        Assert.NotNull(fixture.AuditWriter.Entries[1].AfterData);
    }
}
