using EcoBilling.Control.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace EcoBilling.Control.IntegrationTests.Persistence;

/// <summary>
/// Starts one PostgreSQL container for the whole test run, migrated once. Test classes
/// share it via <see cref="DatabaseCollection"/> so xUnit also serializes them against
/// each other, not just their own methods -- they write to the same tables.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public ControlDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ControlDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new ControlDbContext(options);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Database collection";
}
