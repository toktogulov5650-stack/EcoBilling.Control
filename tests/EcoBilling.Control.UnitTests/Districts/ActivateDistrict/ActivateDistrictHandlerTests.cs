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
        var cache = new FakeCache();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new ActivateDistrictHandler(repository, auditWriter, cache, unitOfWork, new FixedClock(Later));

        var result = await handler.HandleAsync(new ActivateDistrictCommand(DistrictId.New(), Caller), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.not_found", result.Error.Code);
        Assert.Equal("district.activate_failed", auditWriter.Entries[0].Action);
        Assert.Empty(cache.RemovedKeys);
    }

    [Fact]
    public async Task HandleAsync_ActivatesAnInactiveDistrict()
    {
        var district = NewDistrict();
        var repository = new FakeDistrictRepository();
        repository.Seed(district);
        var auditWriter = new FakeAuditWriter();
        var cache = new FakeCache();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new ActivateDistrictHandler(repository, auditWriter, cache, unitOfWork, new FixedClock(Later));

        var result = await handler.HandleAsync(new ActivateDistrictCommand(district.Id, Caller), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(district.IsActive);
        Assert.Equal(Later, district.ActivatedAt);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Equal("district.activated", auditWriter.Entries[0].Action);
        Assert.NotEqual(auditWriter.Entries[0].BeforeData, auditWriter.Entries[0].AfterData);
        Assert.Equal(["district:resolve:BISHKEK-01"], cache.RemovedKeys);
    }

    [Fact]
    public async Task HandleAsync_OnAnAlreadyActiveDistrict_SucceedsAsANoOp_ThatStillGetsAudited()
    {
        // Stage 1 decision: Domain's own invariant (Activate on an active district is
        // an error) stays strict; this handler never reaches that path on a repeat
        // call. Stage 7 decision: the no-op is still audited -- Before and After are
        // identical, which is itself the record that nothing changed.
        //
        // This test cannot spy on District.Activate() directly (District is a concrete
        // Domain entity, not called through a mockable interface), so it instead relies
        // on a fact proven directly from Domain.Activate()'s own source (District.cs):
        // on an already-active district, Activate() ALWAYS fails before mutating
        // anything -- Status/ActivatedAt/UpdatedAt are untouched on that path, and
        // mutation only ever happens together with Result.Success(). That makes
        // "IsSuccess + district.activated action" and "Activate() was never invoked"
        // equivalent outcomes here: if a regression removed the handler's `if
        // (district.IsActive)` short-circuit and called Activate() unconditionally,
        // Domain's guard would turn this into a failure (caught below), and if instead
        // Domain's own invariant were loosened to make Activate() silently idempotent,
        // ActivatedAt/UpdatedAt would be overwritten to `Later` (also caught below).
        var district = NewDistrict();
        district.Activate(Now);
        var originalActivatedAt = district.ActivatedAt;
        var originalUpdatedAt = district.UpdatedAt;
        var repository = new FakeDistrictRepository();
        repository.Seed(district);
        var auditWriter = new FakeAuditWriter();
        var cache = new FakeCache();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new ActivateDistrictHandler(repository, auditWriter, cache, unitOfWork, new FixedClock(Later));

        var result = await handler.HandleAsync(new ActivateDistrictCommand(district.Id, Caller), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(originalActivatedAt, district.ActivatedAt); // unchanged -- Activate() was never called again
        Assert.Equal(originalUpdatedAt, district.UpdatedAt); // unchanged -- second, independent proof of no mutation
        Assert.Equal(1, unitOfWork.SaveChangesCallCount); // the no-op audit entry still commits
        Assert.Equal("district.activated", auditWriter.Entries[0].Action);
        Assert.Equal(auditWriter.Entries[0].BeforeData, auditWriter.Entries[0].AfterData); // identical -- signals "nothing changed"
        Assert.Equal(["district:resolve:BISHKEK-01"], cache.RemovedKeys); // Stage 12: invalidated even on the no-op (harmless).
    }
}
