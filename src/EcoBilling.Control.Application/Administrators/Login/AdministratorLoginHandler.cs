using System.Text.Json;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Auditing;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.Application.Administrators.Login;

/// <summary>
/// Authenticates a system administrator.
/// </summary>
/// <remarks>
/// Unknown email, wrong password, an inactive account, and a lockout (Stage 10) all
/// collapse onto the same <see cref="AdministratorErrors.InvalidCredentials"/> failure
/// by the time this reaches the Api layer -- stricter than ResolveDistrict's
/// not-found/inactive split (Stage 3), because doc section 21.2 requires that errors
/// never reveal whether a specific administrator email exists, and there are few enough
/// administrators that login is a realistic, high-value brute-force/credential-stuffing
/// target. Internally this handler still distinguishes <see cref="AdministratorErrors.Inactive"/>
/// and <see cref="AdministratorErrors.LockedOut"/> and audits them separately (Stage 7,
/// Stage 10), even though the client-visible response never does.
///
/// An unknown-email failure is also audited (Stage 7 decision), with
/// <c>AdministratorId = null</c> (there is no account to attach it to) and the attempted
/// email recorded in <c>AfterData</c> -- it is not a secret, and a pattern of attempts
/// against non-existent accounts is exactly the signal an audit trail exists to catch.
/// </remarks>
public sealed class AdministratorLoginHandler(
    IAdministratorRepository administrators,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher passwordHasher,
    IAccessTokenIssuer accessTokenIssuer,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<AdministratorLoginCommand, AdministratorLoginResult>
{
    public async Task<Result<AdministratorLoginResult>> HandleAsync(
        AdministratorLoginCommand command,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var emailResult = AdministratorEmail.Create(command.Email);
        var password = command.Password ?? string.Empty;

        Administrator? administrator = emailResult.IsSuccess
            ? await administrators.GetByNormalizedEmailAsync(emailResult.Value.Normalized, cancellationToken)
            : null;

        // Always run exactly one password verification, whether the email was invalid,
        // unknown, or real -- so the response time cannot reveal which case occurred.
        // Checking a lockout further below is a cheap in-memory comparison, not a hash,
        // so it adds no further timing signal regardless of where it happens relative to
        // this call -- what matters is that this call itself is never skipped.
        var passwordMatches = administrator is not null
            ? passwordHasher.Verify(administrator.PasswordHash, password)
            : passwordHasher.Verify(passwordHasher.DummyHash, password);

        if (administrator is null)
        {
            return await FailUnknownEmailAsync(command, now, cancellationToken);
        }

        if (administrator.IsLockedOut(now))
        {
            // Correct or wrong password, it makes no difference: a lockout that a
            // correct password could bypass would not be a lockout, and the counters
            // are deliberately untouched here (Stage 10, section 2) -- an attacker
            // hammering the endpoint during an active lockout must not be able to
            // extend it or advance escalation further.
            return await FailAsync(administrator, AdministratorErrors.LockedOut, now, cancellationToken);
        }

        if (!passwordMatches)
        {
            var lockedOutJustNow = administrator.RecordFailedLoginAttempt(now);

            return lockedOutJustNow
                ? await FailWithLockoutTriggeredAsync(administrator, now, cancellationToken)
                : await FailAsync(administrator, AdministratorErrors.InvalidCredentials, now, cancellationToken);
        }

        if (!administrator.IsActive)
        {
            return await FailAsync(administrator, AdministratorErrors.Inactive, now, cancellationToken);
        }

        administrator.RecordLogin(now);

        var accessToken = accessTokenIssuer.Issue(administrator, now);
        var issued = RefreshToken.IssueNew(RefreshTokenId.New(), administrator.Id, now, RefreshTokenPolicy.Lifetime);
        refreshTokens.Add(issued.Token);

        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            administrator.Id.Value,
            AuditActions.AdministratorLoginSucceeded,
            "Administrator",
            administrator.Id.Value.ToString(),
            BeforeData: null,
            AfterData: null,
            CorrelationId: null,
            IpAddress: null,
            UserAgent: null,
            now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new AdministratorLoginResult(
            accessToken.Value,
            accessToken.ExpiresAt,
            issued.RawValue,
            issued.Token.ExpiresAt));
    }

    private async Task<Result<AdministratorLoginResult>> FailUnknownEmailAsync(
        AdministratorLoginCommand command, DateTimeOffset now, CancellationToken cancellationToken)
    {
        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            AdministratorId: null,
            AuditActions.AdministratorLoginFailed,
            "Administrator",
            EntityId: null,
            BeforeData: null,
            AfterData: JsonSerializer.Serialize(new { attemptedEmail = command.Email }),
            CorrelationId: null,
            IpAddress: null,
            UserAgent: null,
            now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Failure<AdministratorLoginResult>(AdministratorErrors.InvalidCredentials);
    }

    /// <summary>
    /// Every known-account failure except the one that trips the lockout threshold --
    /// wrong password, inactive account, or an attempt during an already-active lockout.
    /// Always audited as <see cref="AuditActions.AdministratorLoginFailed"/>, with the
    /// real internal reason in <c>AfterData</c> even though the client-visible response
    /// never distinguishes it (this method's own <paramref name="error"/> is collapsed
    /// to <see cref="AdministratorErrors.InvalidCredentials"/> by the Api layer).
    /// </summary>
    private async Task<Result<AdministratorLoginResult>> FailAsync(
        Administrator administrator, Error error, DateTimeOffset now, CancellationToken cancellationToken)
    {
        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            administrator.Id.Value,
            AuditActions.AdministratorLoginFailed,
            "Administrator",
            administrator.Id.Value.ToString(),
            BeforeData: null,
            AfterData: JsonSerializer.Serialize(new { errorCode = error.Code }),
            CorrelationId: null,
            IpAddress: null,
            UserAgent: null,
            now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Failure<AdministratorLoginResult>(error);
    }

    /// <summary>
    /// The specific failure that just tripped the lockout threshold (Stage 10, section
    /// 4): audited as <see cref="AuditActions.AdministratorLockedOut"/> only, not also a
    /// separate <see cref="AuditActions.AdministratorLoginFailed"/> entry for the same
    /// event -- that would be redundant noise for one attempt.
    /// </summary>
    private async Task<Result<AdministratorLoginResult>> FailWithLockoutTriggeredAsync(
        Administrator administrator, DateTimeOffset now, CancellationToken cancellationToken)
    {
        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            administrator.Id.Value,
            AuditActions.AdministratorLockedOut,
            "Administrator",
            administrator.Id.Value.ToString(),
            BeforeData: null,
            AfterData: JsonSerializer.Serialize(new
            {
                lockedUntil = administrator.LockedUntil,
                consecutiveLockouts = administrator.ConsecutiveLockouts,
            }),
            CorrelationId: null,
            IpAddress: null,
            UserAgent: null,
            now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Failure<AdministratorLoginResult>(AdministratorErrors.LockedOut);
    }
}
