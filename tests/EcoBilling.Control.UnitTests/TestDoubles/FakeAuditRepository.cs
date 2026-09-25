using EcoBilling.Control.Application.Abstractions;

namespace EcoBilling.Control.UnitTests.TestDoubles;

internal sealed class FakeAuditRepository(IReadOnlyList<AuditEntry> entries) : IAuditRepository
{
    public Task<PagedResult<AuditEntry>> ListAsync(int skip, int take, CancellationToken cancellationToken)
    {
        var ordered = entries.OrderByDescending(e => e.CreatedAt).ToList();

        return Task.FromResult(new PagedResult<AuditEntry>(ordered.Skip(skip).Take(take).ToList(), ordered.Count));
    }
}
