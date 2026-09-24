using EcoBilling.Control.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace EcoBilling.Control.EndToEndTests;

/// <summary>
/// Starts one PostgreSQL container for the whole test run and applies migrations to it
/// once. Test classes that need a real database share this fixture through
/// <see cref="DatabaseCollection"/> rather than each starting their own container, which
/// would make the suite far slower for no isolation benefit real tables already give.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<ControlDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using var dbContext = new ControlDbContext(options);
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>
/// Groups every test class that shares <see cref="DatabaseFixture"/> into one xUnit
/// collection, so xUnit also runs those classes sequentially against each other, not
/// just the test methods within one class. Required because they share one database:
/// running two of them at once could interleave writes to the same table.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Database collection";
}
