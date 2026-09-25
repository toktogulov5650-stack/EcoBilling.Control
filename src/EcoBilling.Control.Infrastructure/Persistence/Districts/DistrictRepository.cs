using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Districts;
using Microsoft.EntityFrameworkCore;

namespace EcoBilling.Control.Infrastructure.Persistence.Districts;

/// <summary>PostgreSQL-backed <see cref="IDistrictRepository"/>.</summary>
public sealed class DistrictRepository(ControlDbContext dbContext) : IDistrictRepository
{
    public Task<District?> GetByNormalizedCodeAsync(string normalizedCode, CancellationToken cancellationToken) =>
        dbContext.Districts
            .AsNoTracking() // ResolveDistrict is a high-frequency read path (doc, section 17.1); nothing here is ever updated through this query.
            .SingleOrDefaultAsync(d => d.NormalizedCode == normalizedCode, cancellationToken);

    public Task<District?> GetByIdAsync(DistrictId id, CancellationToken cancellationToken) =>
        dbContext.Districts
            // Tracked (no AsNoTracking): callers mutate the returned entity through its
            // own methods and persist via IUnitOfWork.SaveChangesAsync, with no separate
            // update call.
            .SingleOrDefaultAsync(d => d.Id == id, cancellationToken);

    public void Add(District district) => dbContext.Districts.Add(district);

    public async Task<PagedResult<District>> ListAsync(int skip, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.Districts.AsNoTracking().OrderBy(d => d.NormalizedCode);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip(skip).Take(take).ToListAsync(cancellationToken);

        return new PagedResult<District>(items, totalCount);
    }
}
