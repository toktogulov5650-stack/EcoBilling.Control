using EcoBilling.Control.Application.Abstractions;

namespace EcoBilling.Control.UnitTests.TestDoubles;

/// <summary>
/// Records how many times a handler committed, so a test can assert a no-op path
/// (e.g. idempotent activate on an already-active district) genuinely never writes.
/// </summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCallCount++;

        return Task.CompletedTask;
    }
}
