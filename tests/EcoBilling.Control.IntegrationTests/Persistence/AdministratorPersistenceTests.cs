using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Infrastructure.Persistence.Administrators;
using Microsoft.EntityFrameworkCore;

namespace EcoBilling.Control.IntegrationTests.Persistence;

/// <summary>
/// Verifies EF Core mapping and the unique index for <see cref="Administrator"/> and
/// <see cref="RefreshToken"/> against a real PostgreSQL container, the same rigor
/// applied to <c>District</c> (Stage 4).
/// </summary>
/// <remarks>Every test uses its own email/administrator; the fixture's table is shared across the whole run.</remarks>
[Collection(DatabaseCollection.Name)]
public sealed class AdministratorPersistenceTests(DatabaseFixture database)
{
    private static Administrator NewAdministrator(string email) =>
        Administrator.Create(
            AdministratorId.New(),
            AdministratorEmail.Create(email).Value,
            "Test administrator",
            "some-pbkdf2-hash",
            DateTimeOffset.UtcNow).Value;

    [Fact]
    public async Task UniqueIndex_PreventsTwoAdministratorsWithTheSameNormalizedEmail()
    {
        await using var firstContext = database.CreateDbContext();
        firstContext.Administrators.Add(NewAdministrator("dupe@example.com"));
        await firstContext.SaveChangesAsync();

        await using var secondContext = database.CreateDbContext();
        secondContext.Administrators.Add(NewAdministrator("dupe@example.com"));

        await Assert.ThrowsAsync<DbUpdateException>(() => secondContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Repository_RoundTripsAnAdministrator()
    {
        var administrator = NewAdministrator("roundtrip@example.com");

        await using var writeContext = database.CreateDbContext();
        new AdministratorRepository(writeContext).Add(administrator);
        await writeContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var found = await new AdministratorRepository(readContext)
            .GetByNormalizedEmailAsync("roundtrip@example.com", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(administrator.Id, found.Id);
        Assert.Equal("some-pbkdf2-hash", found.PasswordHash);
        Assert.True(found.IsActive);
    }

    [Fact]
    public async Task MutatingATrackedAdministrator_ThenSavingChanges_PersistsTheChange()
    {
        // Same proof as District's (Stage 4/5): RecordLogin mutates only through a
        // private setter, with no explicit Update() call, yet must still persist.
        var administrator = NewAdministrator("mutate@example.com");

        await using var writeContext = database.CreateDbContext();
        writeContext.Administrators.Add(administrator);
        await writeContext.SaveChangesAsync();

        await using var mutateContext = database.CreateDbContext();
        var tracked = await new AdministratorRepository(mutateContext).GetByIdAsync(administrator.Id, CancellationToken.None);
        Assert.NotNull(tracked);
        // Truncated to microsecond precision: PostgreSQL's timestamptz stores
        // microseconds, but DateTimeOffset.Ticks is 100-nanosecond resolution, so an
        // un-truncated "now" can round-trip with its last decimal digit changed.
        var loginAt = TruncateToMicroseconds(DateTimeOffset.UtcNow);
        tracked.RecordLogin(loginAt);
        await mutateContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var reloaded = await new AdministratorRepository(readContext).GetByIdAsync(administrator.Id, CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(loginAt, reloaded.LastLoginAt);
    }

    [Fact]
    public async Task RefreshTokenRepository_RoundTripsATokenAndFindsItByHash()
    {
        await using var writeContext = database.CreateDbContext();
        var administrator = NewAdministrator("refresh-owner@example.com");
        writeContext.Administrators.Add(administrator);
        var issued = RefreshToken.IssueNew(RefreshTokenId.New(), administrator.Id, DateTimeOffset.UtcNow, TimeSpan.FromDays(14));
        new RefreshTokenRepository(writeContext).Add(issued.Token);
        await writeContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var found = await new RefreshTokenRepository(readContext)
            .GetByTokenHashAsync(RefreshToken.HashRawValue(issued.RawValue), CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(administrator.Id, found.AdministratorId);
    }

    [Fact]
    public async Task RefreshTokenRepository_GetActiveByAdministratorId_ExcludesRevokedAndExpiredTokens()
    {
        var administratorId = AdministratorId.New();
        var now = DateTimeOffset.UtcNow;

        await using var writeContext = database.CreateDbContext();
        writeContext.Administrators.Add(Administrator.Create(
            administratorId, AdministratorEmail.Create("active-lookup@example.com").Value, "Test", "hash", now).Value);

        var active = RefreshToken.IssueNew(RefreshTokenId.New(), administratorId, now, TimeSpan.FromDays(14));
        var revoked = RefreshToken.IssueNew(RefreshTokenId.New(), administratorId, now, TimeSpan.FromDays(14));
        revoked.Token.Revoke(now);
        var expired = RefreshToken.IssueNew(RefreshTokenId.New(), administratorId, now, TimeSpan.FromMinutes(1));

        var repository = new RefreshTokenRepository(writeContext);
        repository.Add(active.Token);
        repository.Add(revoked.Token);
        repository.Add(expired.Token);
        await writeContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var found = await new RefreshTokenRepository(readContext)
            .GetActiveByAdministratorIdAsync(administratorId, now.AddMinutes(5), CancellationToken.None);

        Assert.Single(found);
        Assert.Equal(active.Token.Id, found[0].Id);
    }

    [Fact]
    public async Task DeletingAnAdministrator_CascadesToTheirRefreshTokens()
    {
        var now = DateTimeOffset.UtcNow;
        var administrator = NewAdministrator("cascade@example.com");

        await using var writeContext = database.CreateDbContext();
        writeContext.Administrators.Add(administrator);
        var issued = RefreshToken.IssueNew(RefreshTokenId.New(), administrator.Id, now, TimeSpan.FromDays(14));
        writeContext.RefreshTokens.Add(issued.Token);
        await writeContext.SaveChangesAsync();

        await using var deleteContext = database.CreateDbContext();
        var toDelete = await deleteContext.Administrators.SingleAsync(a => a.Id == administrator.Id);
        deleteContext.Administrators.Remove(toDelete);
        await deleteContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var remainingToken = await readContext.RefreshTokens.SingleOrDefaultAsync(t => t.Id == issued.Token.Id);

        Assert.Null(remainingToken);
    }

    [Fact]
    public async Task Repository_RoundTripsLockoutBookkeeping_Stage10()
    {
        var now = TruncateToMicroseconds(DateTimeOffset.UtcNow);
        var administrator = NewAdministrator("lockout-roundtrip@example.com");
        for (var i = 0; i < 4; i++)
        {
            administrator.RecordFailedLoginAttempt(now.AddSeconds(i));
        }
        administrator.RecordFailedLoginAttempt(now.AddSeconds(4)); // 5th -- triggers the lockout.

        await using var writeContext = database.CreateDbContext();
        writeContext.Administrators.Add(administrator);
        await writeContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var found = await new AdministratorRepository(readContext).GetByIdAsync(administrator.Id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(0, found.FailedLoginAttempts); // reset by the lockout trigger itself.
        Assert.Equal(1, found.ConsecutiveLockouts);
        Assert.Equal(administrator.LockedUntil, found.LockedUntil);
        Assert.Equal(now.AddSeconds(4), found.LastFailedLoginAt);
        Assert.True(found.IsLockedOut(now.AddSeconds(4)));
    }

    [Fact]
    public async Task Repository_RoundTripsAnAdministratorWithNoLockoutHistory_AsAllDefaults()
    {
        var administrator = NewAdministrator("no-lockout-history@example.com");

        await using var writeContext = database.CreateDbContext();
        writeContext.Administrators.Add(administrator);
        await writeContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var found = await new AdministratorRepository(readContext).GetByIdAsync(administrator.Id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(0, found.FailedLoginAttempts);
        Assert.Equal(0, found.ConsecutiveLockouts);
        Assert.Null(found.LockedUntil);
        Assert.Null(found.LastFailedLoginAt);
    }

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % 10), value.Offset);
}
