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
}
