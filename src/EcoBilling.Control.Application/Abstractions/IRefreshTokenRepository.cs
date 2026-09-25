using EcoBilling.Control.Domain.Administrators;

namespace EcoBilling.Control.Application.Abstractions;

/// <summary>Access to refresh-token sessions.</summary>
public interface IRefreshTokenRepository
{
    /// <summary>
    /// Looks up a refresh token by the hash of its raw value, tracked, in any state
    /// (active, expired or already revoked) -- the caller decides how each case is
    /// handled, including reuse detection on an already-revoked token.
    /// </summary>
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Every currently-active (not expired, not revoked) token for an administrator, as
    /// of <paramref name="now"/>. Used only by reuse detection to revoke every other
    /// session when a stolen, already-rotated-out token is replayed.
    /// </summary>
    Task<IReadOnlyList<RefreshToken>> GetActiveByAdministratorIdAsync(
        AdministratorId administratorId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Stages a new refresh token for insertion. Nothing persists until <see cref="IUnitOfWork.SaveChangesAsync"/>.</summary>
    void Add(RefreshToken refreshToken);
}
