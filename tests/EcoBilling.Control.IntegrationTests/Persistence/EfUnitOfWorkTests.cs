using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Domain.Provisioning;
using EcoBilling.Control.Infrastructure.Persistence;
using EcoBilling.Control.Infrastructure.Persistence.Provisioning;
using Microsoft.EntityFrameworkCore;

namespace EcoBilling.Control.IntegrationTests.Persistence;

/// <summary>
/// Verifies <see cref="EfUnitOfWork"/>'s two methods against real PostgreSQL (Stage 9):
/// <see cref="EfUnitOfWork.SaveChangesAsync"/> is unchanged from before this stage --
/// still throws a raw <see cref="DbUpdateException"/> on any storage failure, for every
/// handler that isn't CreateDirector -- and the new, narrowly-scoped
/// <see cref="EfUnitOfWork.SaveChangesOrConflictAsync"/> reports a unique-constraint
/// violation as a failed <see cref="Result"/> instead.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class EfUnitOfWorkTests(DatabaseFixture database)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static District NewDistrict(string code) =>
        District.Create(
            DistrictId.New(),
            DistrictCode.Create(code).Value,
            "Test district",
            TrustedApiUrl.Create("https://district.example.com/api").Value,
            Now).Value;

    private async Task<District> SeedDistrictAsync(string code)
    {
        var district = NewDistrict(code);
        await using var seedContext = database.CreateDbContext();
        seedContext.Districts.Add(district);
        await seedContext.SaveChangesAsync();

        return district;
    }

    [Fact]
    public async Task SaveChangesAsync_OnAUniqueViolation_ThrowsTheRawDbUpdateException_Unchanged()
    {
        // Proves the revert: SaveChangesAsync's own contract is exactly what it was
        // before this stage -- every handler except CreateDirector still sees a raw
        // DbUpdateException on a storage conflict, not any new translated type.
        var district = await SeedDistrictAsync("EFUOW-01");

        var first = ProvisioningOperation.Create(
            ProvisioningOperationId.New(), district.Id, ProvisioningOperationType.DirectorCreation, Now);
        await using var firstContext = database.CreateDbContext();
        new ProvisioningOperationRepository(firstContext).Add(first);
        await new EfUnitOfWork(firstContext).SaveChangesAsync(CancellationToken.None);

        var second = ProvisioningOperation.Create(
            ProvisioningOperationId.New(), district.Id, ProvisioningOperationType.DirectorCreation, Now);
        await using var secondContext = database.CreateDbContext();
        new ProvisioningOperationRepository(secondContext).Add(second);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => new EfUnitOfWork(secondContext).SaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveChangesAsync_OnASuccessfulWrite_CommitsNormally()
    {
        var district = NewDistrict("EFUOW-02");

        await using var writeContext = database.CreateDbContext();
        writeContext.Districts.Add(district);

        await new EfUnitOfWork(writeContext).SaveChangesAsync(CancellationToken.None); // must not throw.

        await using var readContext = database.CreateDbContext();
        Assert.NotNull(await readContext.Districts.FindAsync(district.Id));
    }

    [Fact]
    public async Task SaveChangesOrConflictAsync_OnAUniqueViolation_ReturnsAFailedResult_WithoutThrowing()
    {
        var district = await SeedDistrictAsync("EFUOW-03");

        var first = ProvisioningOperation.Create(
            ProvisioningOperationId.New(), district.Id, ProvisioningOperationType.DirectorCreation, Now);
        await using var firstContext = database.CreateDbContext();
        new ProvisioningOperationRepository(firstContext).Add(first);
        await new EfUnitOfWork(firstContext).SaveChangesOrConflictAsync(CancellationToken.None);

        var second = ProvisioningOperation.Create(
            ProvisioningOperationId.New(), district.Id, ProvisioningOperationType.DirectorCreation, Now);
        await using var secondContext = database.CreateDbContext();
        new ProvisioningOperationRepository(secondContext).Add(second);

        var result = await new EfUnitOfWork(secondContext).SaveChangesOrConflictAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UnitOfWorkErrors.ConcurrencyConflict.Code, result.Error.Code);
    }

    [Fact]
    public async Task SaveChangesOrConflictAsync_OnASuccessfulWrite_ReturnsSuccess_AndCommitsNormally()
    {
        var district = NewDistrict("EFUOW-04");

        await using var writeContext = database.CreateDbContext();
        writeContext.Districts.Add(district);

        var result = await new EfUnitOfWork(writeContext).SaveChangesOrConflictAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);

        await using var readContext = database.CreateDbContext();
        Assert.NotNull(await readContext.Districts.FindAsync(district.Id));
    }
}
