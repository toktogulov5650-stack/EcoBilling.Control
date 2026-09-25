using System.Text.Json;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Auditing;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Domain.Common;
using DomainRefreshToken = EcoBilling.Control.Domain.Administrators.RefreshToken;

namespace EcoBilling.Control.Application.Administrators.RefreshAdministratorSession;

/// <summary>
/// Exchanges a refresh token for a new access token, rotating the refresh token in the
/// same call: the presented token is revoked and a new one issued, so it cannot be used
/// a second time.
/// </summary>
/// <remarks>
/// Reuse detection: if the presented token was already revoked (as opposed to merely
/// expired), that specifically means someone is presenting a token a legitimate rotation
/// already superseded -- a signal of theft or replay, not an innocent stale token. The
/// response is to revoke every other currently-active session for that administrator,
/// forcing a full re-login everywhere, on the theory that an attacker who captured one
/// refresh token may have captured others issued around the same time.
///
/// Only the reuse-detection case is audited (Stage 7 decision): it is a genuine security
/// event. Routine rotation on every ordinary refresh is deliberately not audited --
/// it happens roughly every 15 minutes per active session, and would be noise, not signal.
/// </remarks>
public sealed class RefreshAdministratorSessionHandler(
    IAdministratorRepository administrators,
    IRefreshTokenRepository refreshTokens,
    IAccessTokenIssuer accessTokenIssuer,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<RefreshAdministratorSessionCommand, RefreshAdministratorSessionResult>
{
    public async Task<Result<RefreshAdministratorSessionResult>> HandleAsync(
        RefreshAdministratorSessionCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(command.RefreshToken))
        {
            return Result.Failure<RefreshAdministratorSessionResult>(AdministratorErrors.InvalidRefreshToken);
        }

        var hash = DomainRefreshToken.HashRawValue(command.RefreshToken);
        var presented = await refreshTokens.GetByTokenHashAsync(hash, cancellationToken);

        if (presented is null)
        {
            return Result.Failure<RefreshAdministratorSessionResult>(AdministratorErrors.InvalidRefreshToken);
        }

        var now = clock.UtcNow;

        if (presented.RevokedAt is not null)
        {
            // Reuse of an already-rotated-out token: revoke everything else this
            // administrator currently has active, then reject this attempt too. The
            // event itself is audited regardless of whether any other session existed
            // to revoke -- the replay attempt is the security-relevant fact.
            var active = await refreshTokens.GetActiveByAdministratorIdAsync(presented.AdministratorId, now, cancellationToken);

            foreach (var token in active)
            {
                token.Revoke(now);
            }

            auditWriter.Write(new AuditEntry(
                Guid.CreateVersion7(),
                presented.AdministratorId.Value,
                AuditActions.AdministratorSessionReuseDetected,
                "Administrator",
                presented.AdministratorId.Value.ToString(),
                BeforeData: null,
                AfterData: JsonSerializer.Serialize(new { revokedSessionCount = active.Count }),
                CorrelationId: null,
                IpAddress: null,
                UserAgent: null,
                now));

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<RefreshAdministratorSessionResult>(AdministratorErrors.InvalidRefreshToken);
        }

        if (!presented.IsActive(now)) // naturally expired, never revoked -- an ordinary stale token, not a reuse signal.
        {
            return Result.Failure<RefreshAdministratorSessionResult>(AdministratorErrors.InvalidRefreshToken);
        }

        var administrator = await administrators.GetByIdAsync(presented.AdministratorId, cancellationToken);

        if (administrator is null || !administrator.IsActive)
        {
            return Result.Failure<RefreshAdministratorSessionResult>(AdministratorErrors.InvalidRefreshToken);
        }

        var issued = DomainRefreshToken.IssueNew(RefreshTokenId.New(), administrator.Id, now, RefreshTokenPolicy.Lifetime);
        presented.Revoke(now, issued.Token.Id);
        refreshTokens.Add(issued.Token);

        var accessToken = accessTokenIssuer.Issue(administrator, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new RefreshAdministratorSessionResult(
            accessToken.Value,
            accessToken.ExpiresAt,
            issued.RawValue,
            issued.Token.ExpiresAt));
    }
}
