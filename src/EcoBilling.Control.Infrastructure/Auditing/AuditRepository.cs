using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EcoBilling.Control.Infrastructure.Auditing;

/// <summary>PostgreSQL-backed <see cref="IAuditRepository"/>.</summary>
public sealed class AuditRepository(ControlDbContext dbContext) : IAuditRepository
{
    public async Task<PagedResult<AuditEntry>> ListAsync(int skip, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.Set<AuditEntry>().AsNoTracking().OrderByDescending(e => e.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip(skip).Take(take).ToListAsync(cancellationToken);

        return new PagedResult<AuditEntry>(items, totalCount);
    }
}
