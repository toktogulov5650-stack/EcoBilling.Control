using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Infrastructure.Persistence.Districts;
using Microsoft.EntityFrameworkCore;

namespace EcoBilling.Control.IntegrationTests.Persistence;

/// <summary>
/// Verifies EF Core mapping, migrations and the unique index against a real PostgreSQL
/// container (brief, section 22: "prefer a real containerized PostgreSQL over EF Core's
/// InMemory provider").
/// </summary>
/// <remarks>
/// Every test uses its own district code: the container and its Districts table are
/// shared across the whole test run (a fresh container per test would be far slower for
/// no real isolation benefit), and nothing here truncates the table between tests.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class DistrictPersistenceTests(DatabaseFixture database)
{
    private static District NewDistrict(string code, string apiBaseUrl = "https://district.example.com/api")
    {
        return District.Create(
            DistrictId.New(),
            DistrictCode.Create(code).Value,
            "Test district",
            TrustedApiUrl.Create(apiBaseUrl).Value,
            DateTimeOffset.UtcNow).Value;
    }

    [Fact]
    public async Task Migrations_ApplyCleanly_AndCreateTheDistrictsTable()
    {
        await using var dbContext = database.CreateDbContext();

        var applied = await dbContext.Database.GetAppliedMigrationsAsync();

        // Applied migration ids carry their timestamp prefix (e.g. "20260924104959_InitialCreate"),
        // so this checks for that suffix rather than an exact name match.
        Assert.Contains(applied, id => id.EndsWith("InitialCreate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UniqueIndex_PreventsTwoDistrictsWithTheSameNormalizedCode()
    {
        await using var firstContext = database.CreateDbContext();
        firstContext.Districts.Add(NewDistrict("DUPE-01"));
        await firstContext.SaveChangesAsync();

        // A different row (different Id / primary key) but the same NormalizedCode --
        // only the database's unique index can catch this; EF's own change tracker has
        // no reason to object, since nothing here violates the primary key.
        await using var secondContext = database.CreateDbContext();
        secondContext.Districts.Add(NewDistrict("DUPE-01"));

        await Assert.ThrowsAsync<DbUpdateException>(() => secondContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Repository_RoundTripsAllValuesForAnActiveDistrict()
    {
        var district = NewDistrict("BISHKEK-10", "https://district-10.example.com/api");
        district.Activate(DateTimeOffset.UtcNow);

        await using var writeContext = database.CreateDbContext();
        writeContext.Districts.Add(district);
        await writeContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var repository = new DistrictRepository(readContext);

        var found = await repository.GetByNormalizedCodeAsync("BISHKEK-10", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(district.Id, found.Id);
        Assert.Equal("BISHKEK-10", found.NormalizedCode);
        Assert.Equal("https://district-10.example.com/api", found.ApiBaseUrl.ToString());
        Assert.True(found.IsActive);
        Assert.Equal(DistrictStatus.Active, found.Status);
    }

    [Fact]
    public async Task Repository_ReturnsNullForAnUnknownCode()
    {
        await using var readContext = database.CreateDbContext();
        var repository = new DistrictRepository(readContext);

        var found = await repository.GetByNormalizedCodeAsync("NEVER-SEEDED-01", CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task Repository_DoesNotFilterOutAnInactiveDistrict()
    {
        // Mirrors the contract already unit-tested against the fake repository (Stage 2):
        // the repository must not hide an inactive district, since the caller needs to
        // distinguish district.not_found from district.inactive itself.
        var district = NewDistrict("BISHKEK-11"); // never activated

        await using var writeContext = database.CreateDbContext();
        writeContext.Districts.Add(district);
        await writeContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var repository = new DistrictRepository(readContext);

        var found = await repository.GetByNormalizedCodeAsync("BISHKEK-11", CancellationToken.None);

        Assert.NotNull(found);
        Assert.False(found.IsActive);
    }

    [Fact]
    public async Task Repository_Add_ThenGetByNormalizedCode_FindsTheCreatedDistrict()
    {
        // The full CreateDistrict write path: Add() stages the entity, nothing reaches
        // Postgres until SaveChangesAsync -- exercised here through the repository
        // itself, not by reaching into the DbContext directly as the earlier tests do.
        var district = NewDistrict("BISHKEK-12", "https://district-12.example.com/api");

        await using var writeContext = database.CreateDbContext();
        new DistrictRepository(writeContext).Add(district);
        await writeContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var found = await new DistrictRepository(readContext).GetByNormalizedCodeAsync("BISHKEK-12", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(district.Id, found.Id);
    }

    [Fact]
    public async Task Repository_GetByIdAsync_FindsTheSeededDistrict()
    {
        var district = NewDistrict("BISHKEK-13");

        await using var writeContext = database.CreateDbContext();
        writeContext.Districts.Add(district);
        await writeContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var found = await new DistrictRepository(readContext).GetByIdAsync(district.Id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("BISHKEK-13", found.NormalizedCode);
    }

    [Fact]
    public async Task Repository_GetByIdAsync_ReturnsNullForAnUnknownId()
    {
        await using var readContext = database.CreateDbContext();

        var found = await new DistrictRepository(readContext).GetByIdAsync(DistrictId.New(), CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task MutatingATrackedDistrict_ThenSavingChanges_PersistsTheChange()
    {
        // This is the assumption UpdateDistrict, ActivateDistrict and DeactivateDistrict
        // (Stage 5) all lean on: District's properties have only private setters and one
        // (ApiBaseUrl) is a value-object-backed conversion, yet EF Core's snapshot change
        // tracking must still detect and persist a mutation made purely through the
        // aggregate's own methods, with no explicit "Update" call. Proven here against
        // real Postgres, not assumed.
        var district = NewDistrict("BISHKEK-14", "https://district-14.example.com/api");

        await using var writeContext = database.CreateDbContext();
        writeContext.Districts.Add(district);
        await writeContext.SaveChangesAsync();

        await using var mutateContext = database.CreateDbContext();
        var tracked = await new DistrictRepository(mutateContext).GetByIdAsync(district.Id, CancellationToken.None);
        Assert.NotNull(tracked);
        var activatedAt = DateTimeOffset.UtcNow;
        tracked.Activate(activatedAt);
        tracked.Rename("Renamed district", activatedAt);
        tracked.ChangeApiBaseUrl(TrustedApiUrl.Create("https://district-14-new.example.com/api").Value, activatedAt);
        await mutateContext.SaveChangesAsync(); // no repository.Update(...) call -- none exists.

        await using var readContext = database.CreateDbContext();
        var reloaded = await new DistrictRepository(readContext).GetByIdAsync(district.Id, CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.True(reloaded.IsActive);
        Assert.Equal("Renamed district", reloaded.Name);
        Assert.Equal("https://district-14-new.example.com/api", reloaded.ApiBaseUrl.ToString());
    }

    [Fact]
    public async Task Repository_ListAsync_ReturnsAPageOrderedByNormalizedCode_WithTheTotalCountAcrossAllPages()
    {
        // The table is shared across the whole test run, so this asserts the relative
        // order of this test's own three codes within a page large enough to hold
        // everything, rather than assuming absolute positions -- other tests' rows may
        // sort anywhere among them.
        await using var writeContext = database.CreateDbContext();
        var repository = new DistrictRepository(writeContext);
        repository.Add(NewDistrict("ZZZLIST-01"));
        repository.Add(NewDistrict("AAALIST-01"));
        repository.Add(NewDistrict("MMMLIST-01"));
        await writeContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var probe = await new DistrictRepository(readContext).ListAsync(skip: 0, take: 1, CancellationToken.None);
        var fullPage = await new DistrictRepository(readContext)
            .ListAsync(skip: 0, take: probe.TotalCount, CancellationToken.None);

        Assert.True(fullPage.TotalCount >= 3);
        var codes = fullPage.Items.Select(d => d.NormalizedCode).ToList();
        var ourCodes = codes.Where(c => c is "ZZZLIST-01" or "AAALIST-01" or "MMMLIST-01").ToList();

        Assert.Equal(new[] { "AAALIST-01", "MMMLIST-01", "ZZZLIST-01" }, ourCodes);
    }
}
