using EcoBilling.Control.Application.Districts.ActivateDistrict;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.UnitTests.TestDoubles;

namespace EcoBilling.Control.UnitTests.Districts.ActivateDistrict;

public sealed class ActivateDistrictHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(1);
    private static readonly AdministratorId Caller = AdministratorId.New();

    private static District NewDistrict() =>
        District.Create(
            DistrictId.New(),
            DistrictCode.Create("BISHKEK-01").Value,
            "Bishkek district",
            TrustedApiUrl.Create("https://district-01.example.com/api").Value,
            Now).Value;

    [Fact]
    public async Task HandleAsync_ReturnsNotFoundForAnUnknownDistrict()
    {
        var repository = new FakeDistrictRepository();
        var auditWriter = new FakeAuditWriter();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new ActivateDistrictHandler(repository, auditWriter, unitOfWork, new FixedClock(Later));

        var result = await handler.HandleAsync(new ActivateDistrictCommand(DistrictId.New(), Caller), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.not_found", result.Error.Code);
        Assert.Equal("district.activate_failed", auditWriter.Entries[0].Action);
    }

    [Fact]
    public async Task HandleAsync_ActivatesAnInactiveDistrict()
    {
        var district = NewDistrict();
        var repository = new FakeDistrictRepository();
        repository.Seed(district);
        var auditWriter = new FakeAuditWriter();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new ActivateDistrictHandler(repository, auditWriter, unitOfWork, new FixedClock(Later));

        var result = await handler.HandleAsync(new ActivateDistrictCommand(district.Id, Caller), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(district.IsActive);
        Assert.Equal(Later, district.ActivatedAt);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Equal("district.activated", auditWriter.Entries[0].Action);
        Assert.NotEqual(auditWriter.Entries[0].BeforeData, auditWriter.Entries[0].AfterData);
    }

    [Fact]
    public async Task HandleAsync_OnAnAlreadyActiveDistrict_SucceedsAsANoOp_ThatStillGetsAudited()
    {
        // Stage 1 decision: Domain's own invariant (Activate on an active district is
        // an error) stays strict; this handler never reaches that path on a repeat
        // call. Stage 7 decision: the no-op is still audited -- Before and After are
        // identical, which is itself the record that nothing changed.
        var district = NewDistrict();
        district.Activate(Now);
        var originalActivatedAt = district.ActivatedAt;
        var repository = new FakeDistrictRepository();
        repository.Seed(district);
        var auditWriter = new FakeAuditWriter();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new ActivateDistrictHandler(repository, auditWriter, unitOfWork, new FixedClock(Later));

        var result = await handler.HandleAsync(new ActivateDistrictCommand(district.Id, Caller), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(originalActivatedAt, district.ActivatedAt); // unchanged -- Activate() was never called again
        Assert.Equal(1, unitOfWork.SaveChangesCallCount); // the no-op audit entry still commits
        Assert.Equal("district.activated", auditWriter.Entries[0].Action);
        Assert.Equal(auditWriter.Entries[0].BeforeData, auditWriter.Entries[0].AfterData); // identical -- signals "nothing changed"
    }
}
