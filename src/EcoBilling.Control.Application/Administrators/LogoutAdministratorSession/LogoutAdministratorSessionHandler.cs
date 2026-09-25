using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Common;
using DomainRefreshToken = EcoBilling.Control.Domain.Administrators.RefreshToken;

namespace EcoBilling.Control.Application.Administrators.LogoutAdministratorSession;

/// <summary>
/// Revokes the presented refresh token. Idempotent by design, like ActivateDistrict /
/// DeactivateDistrict (Stage 5): a missing, unknown, already-revoked or already-expired
/// token all succeed as a no-op -- the caller's intent ("end this session") is satisfied
/// either way, and failing here would only give an observer a way to distinguish token
/// states through the response.
/// </summary>
/// <remarks>
/// Cannot invalidate an access token already issued before its natural expiry (at most
/// 15 minutes) -- inherent to stateless JWTs with no revocation list, which is out of
/// scope here; accepted, standard behavior, not a gap.
/// </remarks>
public sealed class LogoutAdministratorSessionHandler(
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<LogoutAdministratorSessionCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(LogoutAdministratorSessionCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(command.RefreshToken))
        {
            return Result.Success(Unit.Value);
        }

        var hash = DomainRefreshToken.HashRawValue(command.RefreshToken);
        var token = await refreshTokens.GetByTokenHashAsync(hash, cancellationToken);
        var now = clock.UtcNow;

        if (token is not null && token.IsActive(now))
        {
            token.Revoke(now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(Unit.Value);
    }
}
