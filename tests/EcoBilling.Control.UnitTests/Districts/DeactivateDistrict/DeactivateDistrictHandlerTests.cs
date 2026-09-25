using EcoBilling.Control.Application.Districts.DeactivateDistrict;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.UnitTests.TestDoubles;

namespace EcoBilling.Control.UnitTests.Districts.DeactivateDistrict;

public sealed class DeactivateDistrictHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(1);
    private static readonly AdministratorId Caller = AdministratorId.New();

    private static District NewActiveDistrict()
    {
        var district = District.Create(
            DistrictId.New(),
            DistrictCode.Create("BISHKEK-01").Value,
            "Bishkek district",
            TrustedApiUrl.Create("https://district-01.example.com/api").Value,
            Now).Value;

        district.Activate(Now);

        return district;
    }

    [Fact]
    public async Task HandleAsync_ReturnsNotFoundForAnUnknownDistrict()
    {
        var repository = new FakeDistrictRepository();
        var auditWriter = new FakeAuditWriter();
        var cache = new FakeCache();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new DeactivateDistrictHandler(repository, auditWriter, cache, unitOfWork, new FixedClock(Later));

        var result = await handler.HandleAsync(new DeactivateDistrictCommand(DistrictId.New(), Caller), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.not_found", result.Error.Code);
        Assert.Equal("district.deactivate_failed", auditWriter.Entries[0].Action);
        Assert.Empty(cache.RemovedKeys);
    }

    [Fact]
    public async Task HandleAsync_DeactivatesAnActiveDistrict()
    {
        var district = NewActiveDistrict();
        var repository = new FakeDistrictRepository();
        repository.Seed(district);
        var auditWriter = new FakeAuditWriter();
        var cache = new FakeCache();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new DeactivateDistrictHandler(repository, auditWriter, cache, unitOfWork, new FixedClock(Later));

        var result = await handler.HandleAsync(new DeactivateDistrictCommand(district.Id, Caller), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(district.IsActive);
        Assert.Equal(Later, district.DeactivatedAt);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Equal("district.deactivated", auditWriter.Entries[0].Action);
        Assert.Equal(["district:resolve:BISHKEK-01"], cache.RemovedKeys);
    }

    [Fact]
    public async Task HandleAsync_OnAnAlreadyInactiveDistrict_SucceedsAsANoOp_ThatStillGetsAudited()
    {
        // See the symmetric comment on ActivateDistrictHandlerTests's equivalent test:
        // Domain.Deactivate() (District.cs) fails before mutating anything when the
        // district is already inactive, so mutation and Result.Success() are the same
        // event there too. "IsSuccess + district.deactivated action + unchanged
        // DeactivatedAt/UpdatedAt" is therefore equivalent to "Deactivate() was never
        // invoked" on this path -- a regressed handler guard or a loosened Domain
        // invariant would each be caught by a different assertion below.
        var district = District.Create(
            DistrictId.New(),
            DistrictCode.Create("BISHKEK-01").Value,
            "Bishkek district",
            TrustedApiUrl.Create("https://district-01.example.com/api").Value,
            Now).Value; // never activated
        var originalUpdatedAt = district.UpdatedAt;
        var repository = new FakeDistrictRepository();
        repository.Seed(district);
        var auditWriter = new FakeAuditWriter();
        var cache = new FakeCache();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new DeactivateDistrictHandler(repository, auditWriter, cache, unitOfWork, new FixedClock(Later));

        var result = await handler.HandleAsync(new DeactivateDistrictCommand(district.Id, Caller), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(district.DeactivatedAt); // unchanged -- Deactivate() was never called again
        Assert.Equal(originalUpdatedAt, district.UpdatedAt); // unchanged -- second, independent proof of no mutation
        Assert.Equal(1, unitOfWork.SaveChangesCallCount); // the no-op audit entry still commits
        Assert.Equal("district.deactivated", auditWriter.Entries[0].Action);
        Assert.Equal(auditWriter.Entries[0].BeforeData, auditWriter.Entries[0].AfterData);
        Assert.Equal(["district:resolve:BISHKEK-01"], cache.RemovedKeys); // Stage 12: invalidated even on the no-op (harmless).
    }
}
