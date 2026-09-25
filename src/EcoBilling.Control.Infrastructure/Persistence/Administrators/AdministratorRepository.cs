using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Administrators;
using Microsoft.EntityFrameworkCore;

namespace EcoBilling.Control.Infrastructure.Persistence.Administrators;

/// <summary>PostgreSQL-backed <see cref="IAdministratorRepository"/>.</summary>
public sealed class AdministratorRepository(ControlDbContext dbContext) : IAdministratorRepository
{
    public Task<Administrator?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        dbContext.Administrators
            // Tracked: AdministratorLoginHandler mutates the result on every call, not
            // only on success -- RecordLogin (success) and RecordFailedLoginAttempt
            // (Stage 10) both need SaveChangesAsync to actually persist them. This was
            // AsNoTracking() before Stage 10, which silently meant RecordLogin's
            // LastLoginAt update never persisted either; nothing depended on that being
            // accurate until lockout made cross-request persistence load-bearing and
            // exposed it.
            .SingleOrDefaultAsync(a => a.NormalizedEmail == normalizedEmail, cancellationToken);

    public Task<Administrator?> GetByIdAsync(AdministratorId id, CancellationToken cancellationToken) =>
        dbContext.Administrators
            // Tracked: RecordLogin and any future mutation persist via SaveChangesAsync
            // with no separate update call.
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public void Add(Administrator administrator) => dbContext.Administrators.Add(administrator);
}
