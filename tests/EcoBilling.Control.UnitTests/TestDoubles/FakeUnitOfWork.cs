using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.UnitTests.TestDoubles;

/// <summary>
/// Records how many times a handler committed, so a test can assert a no-op path
/// (e.g. idempotent activate on an already-active district) genuinely never writes.
/// </summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    /// <summary>
    /// What <see cref="SaveChangesOrConflictAsync"/> returns -- defaults to success; a
    /// test overrides this to simulate a losing race against the database (Stage 9)
    /// without a real database.
    /// </summary>
    public Result SaveChangesOrConflictResult { get; set; } = Result.Success();

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCallCount++;

        return Task.CompletedTask;
    }

    public Task<Result> SaveChangesOrConflictAsync(CancellationToken cancellationToken)
    {
        SaveChangesCallCount++;

        return Task.FromResult(SaveChangesOrConflictResult);
    }
}
