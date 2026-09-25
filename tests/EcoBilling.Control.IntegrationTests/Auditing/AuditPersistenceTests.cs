using System.Text.Json;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Infrastructure.Auditing;
using EcoBilling.Control.Infrastructure.Persistence.Districts;
using EcoBilling.Control.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace EcoBilling.Control.IntegrationTests.Auditing;

/// <summary>
/// Verifies <see cref="AuditEntry"/> persistence against real PostgreSQL, including the
/// atomicity guarantee <see cref="IAuditWriter"/>'s design depends on: an audit entry
/// staged alongside a business change that ultimately fails to save does not persist
/// either -- there is no "changed but not audited" or "audited but not changed" state.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AuditPersistenceTests(DatabaseFixture database)
{
    private static AuditEntry NewEntry(string action = "test.action") =>
        new(
            Guid.CreateVersion7(),
            AdministratorId: Guid.NewGuid(),
            action,
            "TestEntity",
            EntityId: Guid.NewGuid().ToString(),
            BeforeData: """{"before":true}""",
            AfterData: """{"after":true}""",
            CorrelationId: "test-correlation",
            IpAddress: "203.0.113.10",
            UserAgent: "integration-test",
            DateTimeOffset.UtcNow);

    [Fact]
    public async Task Repository_RoundTripsAnAuditEntry_IncludingItsJsonbColumns()
    {
        var entry = NewEntry("district.created");

        await using var writeContext = database.CreateDbContext();
        writeContext.Set<AuditEntry>().Add(entry);
        await writeContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var found = await readContext.Set<AuditEntry>().AsNoTracking().SingleAsync(e => e.Id == entry.Id);

        Assert.Equal(entry.AdministratorId, found.AdministratorId);
        Assert.Equal(entry.Action, found.Action);
        Assert.Equal(entry.EntityType, found.EntityType);
        Assert.Equal(entry.EntityId, found.EntityId);

        // jsonb round-trips the *semantic* content, not the exact text: PostgreSQL
        // parses jsonb into a binary form and re-serializes it in its own canonical
        // text on read (here, with a space after each colon that the original input
        // did not have) -- a byte-for-byte string comparison would be a false
        // negative. This is jsonb's normal, documented behavior, not a bug -- json
        // (without the "b") would have preserved the exact input text instead.
        AssertSameJson(entry.BeforeData, found.BeforeData);
        AssertSameJson(entry.AfterData, found.AfterData);

        Assert.Equal(entry.CorrelationId, found.CorrelationId);
        Assert.Equal(entry.IpAddress, found.IpAddress);
        Assert.Equal(entry.UserAgent, found.UserAgent);
    }

    private static void AssertSameJson(string? expected, string? actual)
    {
        if (expected is null)
        {
            Assert.Null(actual);

            return;
        }

        Assert.NotNull(actual);
        Assert.Equal(
            JsonSerializer.Serialize(JsonDocument.Parse(expected).RootElement),
            JsonSerializer.Serialize(JsonDocument.Parse(actual).RootElement));
    }

    [Fact]
    public async Task Repository_RoundTripsAnAuditEntry_WithANullAdministratorId()
    {
        // The unknown-email login and CreateAdministrator-via-CLI cases (Stage 7).
        var entry = NewEntry() with { AdministratorId = null };

        await using var writeContext = database.CreateDbContext();
        writeContext.Set<AuditEntry>().Add(entry);
        await writeContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var found = await readContext.Set<AuditEntry>().AsNoTracking().SingleAsync(e => e.Id == entry.Id);

        Assert.Null(found.AdministratorId);
    }

    [Fact]
    public async Task AuditRepository_ListsEntriesNewestFirst()
    {
        var older = NewEntry("test.older");
        var newer = older with { Id = Guid.CreateVersion7(), Action = "test.newer", CreatedAt = older.CreatedAt.AddMinutes(1) };

        await using var writeContext = database.CreateDbContext();
        writeContext.Set<AuditEntry>().AddRange(older, newer);
        await writeContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var page = await new AuditRepository(readContext).ListAsync(skip: 0, take: 2, CancellationToken.None);

        var ids = page.Items.Select(e => e.Id).ToList();
        Assert.True(ids.IndexOf(newer.Id) < ids.IndexOf(older.Id));
    }

    [Fact]
    public async Task AnAuditEntry_StagedAlongsideABusinessChangeThatFailsToSave_DoesNotPersistEither()
    {
        // Same guarantee District/UpdateDistrict etc. lean on, proven directly against
        // the database rather than just asserted by design: IAuditWriter.Write only
        // stages; the caller's own SaveChangesAsync is what commits both the audit row
        // and the business change in one transaction. Forcing the business change to
        // violate a unique constraint must roll back the audit entry too.
        const string code = "ATOMIC-01";
        var existing = District.Create(
            DistrictId.New(),
            DistrictCode.Create(code).Value,
            "Existing",
            TrustedApiUrl.Create("https://atomic.example.com/api").Value,
            DateTimeOffset.UtcNow).Value;

        await using var seedContext = database.CreateDbContext();
        seedContext.Districts.Add(existing);
        await seedContext.SaveChangesAsync();

        var conflicting = District.Create(
            DistrictId.New(),
            DistrictCode.Create(code).Value, // same NormalizedCode -- violates the unique index
            "Conflicting",
            TrustedApiUrl.Create("https://atomic-2.example.com/api").Value,
            DateTimeOffset.UtcNow).Value;
        var entry = NewEntry("district.created");

        await using var failingContext = database.CreateDbContext();
        new DistrictRepository(failingContext).Add(conflicting);
        failingContext.Set<AuditEntry>().Add(entry);

        await Assert.ThrowsAsync<DbUpdateException>(() => failingContext.SaveChangesAsync());

        await using var readContext = database.CreateDbContext();
        var persistedEntry = await readContext.Set<AuditEntry>().AsNoTracking().SingleOrDefaultAsync(e => e.Id == entry.Id);

        Assert.Null(persistedEntry); // rolled back with the district it accompanied -- neither persisted.
    }

    [Fact]
    public async Task AuditWriter_EnrichesFromTheAmbientHttpContext_WhenOneExists()
    {
        var httpContext = new DefaultHttpContext { TraceIdentifier = "trace-abc-123" };
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.55");
        httpContext.Request.Headers["User-Agent"] = "integration-test-agent";
        var accessor = new HttpContextAccessor { HttpContext = httpContext };

        var entry = NewEntry("test.enriched");

        await using var writeContext = database.CreateDbContext();
        new AuditWriter(writeContext, accessor).Write(entry);
        await writeContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var found = await readContext.Set<AuditEntry>().AsNoTracking().SingleAsync(e => e.Id == entry.Id);

        Assert.Equal("trace-abc-123", found.CorrelationId);
        Assert.Equal("203.0.113.55", found.IpAddress);
        Assert.Equal("integration-test-agent", found.UserAgent);
    }

    [Fact]
    public async Task AuditWriter_LeavesRequestContextFieldsNull_WhenThereIsNoHttpContext()
    {
        // The Provisioning CLI's situation: IHttpContextAccessor is registered (a plain
        // HttpContextAccessor), but nothing ever sets its HttpContext, because there is
        // no HTTP request at all.
        var accessor = new HttpContextAccessor();

        var entry = NewEntry("test.no_http_context");

        await using var writeContext = database.CreateDbContext();
        new AuditWriter(writeContext, accessor).Write(entry);
        await writeContext.SaveChangesAsync();

        await using var readContext = database.CreateDbContext();
        var found = await readContext.Set<AuditEntry>().AsNoTracking().SingleAsync(e => e.Id == entry.Id);

        Assert.Null(found.CorrelationId);
        Assert.Null(found.IpAddress);
        Assert.Null(found.UserAgent);
    }
}
