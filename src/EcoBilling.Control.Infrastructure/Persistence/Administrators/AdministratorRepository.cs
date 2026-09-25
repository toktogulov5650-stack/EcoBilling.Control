using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Administrators;
using Microsoft.EntityFrameworkCore;

namespace EcoBilling.Control.Infrastructure.Persistence.Administrators;

/// <summary>PostgreSQL-backed <see cref="IAdministratorRepository"/>.</summary>
public sealed class AdministratorRepository(ControlDbContext dbContext) : IAdministratorRepository
{
    public Task<Administrator?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        dbContext.Administrators
            .AsNoTracking() // Login is the one high-frequency read here; nothing is updated through this query.
            .SingleOrDefaultAsync(a => a.NormalizedEmail == normalizedEmail, cancellationToken);

    public Task<Administrator?> GetByIdAsync(AdministratorId id, CancellationToken cancellationToken) =>
        dbContext.Administrators
            // Tracked: RecordLogin and any future mutation persist via SaveChangesAsync
            // with no separate update call.
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public void Add(Administrator administrator) => dbContext.Administrators.Add(administrator);
}
