using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Domain.Provisioning;
using EcoBilling.Control.Infrastructure.Persistence.Provisioning;
using EcoBilling.Control.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EcoBilling.Control.IntegrationTests.Provisioning;

/// <summary>Verifies <see cref="ProvisioningOperation"/> mapping and the migration against real PostgreSQL (Stage 8).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProvisioningOperationPersistenceTests(DatabaseFixture database)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static District NewDistrict(string code) =>
        District.Create(
            DistrictId.New(),
            DistrictCode.Create(code).Value,
            "Test district",
            TrustedApiUrl.Create("https://district.example.com/api").Value,
            Now).Value;

    [Fact]
    public async Task Migrations_ApplyCleanly_AndCreateTheProvisioningOperationsTable()
    {
        await using var dbContext = database.CreateDbContext();

        var applied = await dbContext.Database.GetAppliedMigrationsAsync();

        Assert.Contains(applied, id => id.EndsWith("AddProvisioningOperations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Repository_RoundTripsAllValuesForACompletedOperation()
    {
        var district = NewDistrict("PROV-01");

        await using var seedContext = database.CreateDbContext();
        seedContext.Districts.Add(district);
        await seedContext.SaveChangesAsync();

        var operation = ProvisioningOperation.Create(
            ProvisioningOperationId.New(), district.Id, ProvisioningOperationType.DirectorCreation, Now);
        operation.RecordAttemptStarted(Now);
        operation.Complete(Now.AddSeconds(5));

        await using var writeContext = database.CreateDbContext();
        new ProvisioningOperationRepository(writeContext).Add(operation);
        await writeContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var found = await new ProvisioningOperationRepository(readContext).GetByIdAsync(operation.Id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(district.Id, found.DistrictId);
        Assert.Equal(ProvisioningOperationType.DirectorCreation, found.OperationType);
        Assert.Equal(operation.IdempotencyKey, found.IdempotencyKey);
        Assert.Equal(ProvisioningStatus.Completed, found.Status);
        Assert.Equal(1, found.AttemptCount);
        Assert.Null(found.FailedAt);
        Assert.Null(found.LastErrorCode);
    }

    [Fact]
    public async Task Repository_RoundTripsAFailedOperation_IncludingItsErrorCode()
    {
        var district = NewDistrict("PROV-02");

        await using var seedContext = database.CreateDbContext();
        seedContext.Districts.Add(district);
        await seedContext.SaveChangesAsync();

        var operation = ProvisioningOperation.Create(
            ProvisioningOperationId.New(), district.Id, ProvisioningOperationType.PasswordReset, Now);
        operation.RecordAttemptStarted(Now);
        operation.Fail("district.unavailable", Now.AddSeconds(10));

        await using var writeContext = database.CreateDbContext();
        new ProvisioningOperationRepository(writeContext).Add(operation);
        await writeContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var found = await new ProvisioningOperationRepository(readContext).GetByIdAsync(operation.Id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(ProvisioningStatus.Failed, found.Status);
        Assert.Equal("district.unavailable", found.LastErrorCode);
        Assert.NotNull(found.FailedAt);
    }

    [Fact]
    public async Task Repository_GetLatestAsync_ReturnsTheMostRecentOperation_ForThatDistrictAndType()
    {
        var district = NewDistrict("PROV-03");

        await using var seedContext = database.CreateDbContext();
        seedContext.Districts.Add(district);
        await seedContext.SaveChangesAsync();

        var older = ProvisioningOperation.Create(
            ProvisioningOperationId.New(), district.Id, ProvisioningOperationType.PasswordReset, Now);
        older.RecordAttemptStarted(Now);
        older.Complete(Now);

        var newer = ProvisioningOperation.Create(
            ProvisioningOperationId.New(), district.Id, ProvisioningOperationType.PasswordReset, Now.AddMinutes(1));

        await using var writeContext = database.CreateDbContext();
        var writeRepository = new ProvisioningOperationRepository(writeContext);
        writeRepository.Add(older);
        writeRepository.Add(newer);
        await writeContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var latest = await new ProvisioningOperationRepository(readContext)
            .GetLatestAsync(district.Id, ProvisioningOperationType.PasswordReset, CancellationToken.None);

        Assert.NotNull(latest);
        Assert.Equal(newer.Id, latest.Id);
    }

    [Fact]
    public async Task Repository_GetLatestAsync_DoesNotMixOperationTypesOfTheSameDistrict()
    {
        var district = NewDistrict("PROV-04");

        await using var seedContext = database.CreateDbContext();
        seedContext.Districts.Add(district);
        await seedContext.SaveChangesAsync();

        var creation = ProvisioningOperation.Create(
            ProvisioningOperationId.New(), district.Id, ProvisioningOperationType.DirectorCreation, Now);
        var reset = ProvisioningOperation.Create(
            ProvisioningOperationId.New(), district.Id, ProvisioningOperationType.PasswordReset, Now.AddMinutes(1));

        await using var writeContext = database.CreateDbContext();
        var writeRepository = new ProvisioningOperationRepository(writeContext);
        writeRepository.Add(creation);
        writeRepository.Add(reset);
        await writeContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var latestCreation = await new ProvisioningOperationRepository(readContext)
            .GetLatestAsync(district.Id, ProvisioningOperationType.DirectorCreation, CancellationToken.None);

        Assert.NotNull(latestCreation);
        Assert.Equal(creation.Id, latestCreation.Id);
    }

    [Fact]
    public async Task DeletingTheDistrict_CascadesToItsProvisioningOperations()
    {
        var district = NewDistrict("PROV-05");
        var operation = ProvisioningOperation.Create(
            ProvisioningOperationId.New(), district.Id, ProvisioningOperationType.DirectorCreation, Now);

        await using var writeContext = database.CreateDbContext();
        writeContext.Districts.Add(district);
        new ProvisioningOperationRepository(writeContext).Add(operation);
        await writeContext.SaveChangesAsync();

        await using var deleteContext = database.CreateDbContext();
        var tracked = await deleteContext.Districts.SingleAsync(d => d.Id == district.Id);
        deleteContext.Districts.Remove(tracked);
        await deleteContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var found = await new ProvisioningOperationRepository(readContext).GetByIdAsync(operation.Id, CancellationToken.None);

        Assert.Null(found); // cascaded away with the district (FK, ON DELETE CASCADE).
    }

    [Fact]
    public async Task PartialUniqueIndex_PreventsTwoDirectorCreationOperationsForTheSameDistrict()
    {
        // Proves the actual race CreateDirectorHandler can lose against (Stage 9,
        // section 1): two ProvisioningOperation rows of type DirectorCreation for the
        // same district, each with its own distinct IdempotencyKey (so the OTHER unique
        // index, on IdempotencyKey, cannot be what catches this) -- only the database,
        // not the handler's own check-then-act logic, actually prevents the second one.
        var district = NewDistrict("RACE-01");

        await using var seedContext = database.CreateDbContext();
        seedContext.Districts.Add(district);
        await seedContext.SaveChangesAsync();

        var first = ProvisioningOperation.Create(
            ProvisioningOperationId.New(), district.Id, ProvisioningOperationType.DirectorCreation, Now);

        await using var firstContext = database.CreateDbContext();
        new ProvisioningOperationRepository(firstContext).Add(first);
        await firstContext.SaveChangesAsync();

        var second = ProvisioningOperation.Create(
            ProvisioningOperationId.New(), district.Id, ProvisioningOperationType.DirectorCreation, Now);
        Assert.NotEqual(first.IdempotencyKey, second.IdempotencyKey); // sanity: not the IdempotencyKey index.

        await using var secondContext = database.CreateDbContext();
        new ProvisioningOperationRepository(secondContext).Add(second);

        await Assert.ThrowsAsync<DbUpdateException>(() => secondContext.SaveChangesAsync());
    }

    [Fact]
    public async Task PartialUniqueIndex_DoesNotRestrictPasswordResetOperations()
    {
        // The filter excludes PasswordReset entirely (Stage 9): two coexisting reset
        // operations for the same district are legitimate (resets are repeatable,
        // section 9.2), and must not be blocked by this constraint.
        var district = NewDistrict("RACE-02");

        await using var seedContext = database.CreateDbContext();
        seedContext.Districts.Add(district);
        await seedContext.SaveChangesAsync();

        var first = ProvisioningOperation.Create(
            ProvisioningOperationId.New(), district.Id, ProvisioningOperationType.PasswordReset, Now);
        var second = ProvisioningOperation.Create(
            ProvisioningOperationId.New(), district.Id, ProvisioningOperationType.PasswordReset, Now.AddMinutes(1));

        await using var writeContext = database.CreateDbContext();
        var repository = new ProvisioningOperationRepository(writeContext);
        repository.Add(first);
        repository.Add(second);

        await writeContext.SaveChangesAsync(); // must not throw.

        await using var readContext = database.CreateDbContext();
        var readRepository = new ProvisioningOperationRepository(readContext);
        Assert.NotNull(await readRepository.GetByIdAsync(first.Id, CancellationToken.None));
        Assert.NotNull(await readRepository.GetByIdAsync(second.Id, CancellationToken.None));
    }
}
