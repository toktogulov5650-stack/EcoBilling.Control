using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Administrators;
using Microsoft.EntityFrameworkCore;

namespace EcoBilling.Control.Infrastructure.Persistence.Administrators;

/// <summary>PostgreSQL-backed <see cref="IRefreshTokenRepository"/>.</summary>
public sealed class RefreshTokenRepository(ControlDbContext dbContext) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        dbContext.RefreshTokens
            // Tracked: refresh and logout both call Revoke() on what this returns.
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> GetActiveByAdministratorIdAsync(
        AdministratorId administratorId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await dbContext.RefreshTokens
            // Tracked: reuse detection calls Revoke() on every one of these.
            .Where(t => t.AdministratorId == administratorId && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(cancellationToken);

    public void Add(RefreshToken refreshToken) => dbContext.RefreshTokens.Add(refreshToken);
}
