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
/// Unknown email, wrong password, and an inactive account all collapse onto the same
/// <see cref="AdministratorErrors.InvalidCredentials"/> failure by the time this
/// reaches the Api layer -- stricter than ResolveDistrict's not-found/inactive split
/// (Stage 3), because doc section 21.2 requires that errors never reveal whether a
/// specific administrator email exists, and there are few enough administrators that
/// login is a realistic, high-value brute-force/credential-stuffing target. Internally
/// this handler still distinguishes <see cref="AdministratorErrors.Inactive"/> and audits
/// it separately (Stage 7), even though the client-visible response never does.
///
/// An unknown-email failure is also audited (Stage 7 decision), with
/// <c>AdministratorId = null</c> (there is no account to attach it to) and the attempted
/// email recorded in <c>AfterData</c> -- it is not a secret, and a pattern of attempts
/// against non-existent accounts is exactly the signal an audit trail exists to catch.
///
/// NOTE: this scenario ships with none of its three stated protections yet (doc,
/// section 19.2: "Rate limit, lockout, audit"). Rate limiting is Stage 14; lockout is
/// Stage 1's still-undecided open question. It must not be exposed to real/public
/// traffic until Stage 14 and the lockout policy land -- the same standing caveat
/// already on ResolveDistrict (Stage 3).
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
        var passwordMatches = administrator is not null
            ? passwordHasher.Verify(administrator.PasswordHash, password)
            : passwordHasher.Verify(passwordHasher.DummyHash, password);

        if (administrator is null || !passwordMatches)
        {
            auditWriter.Write(new AuditEntry(
                Guid.CreateVersion7(),
                administrator?.Id.Value,
                AuditActions.AdministratorLoginFailed,
                "Administrator",
                administrator?.Id.Value.ToString(),
                BeforeData: null,
                AfterData: administrator is null
                    ? JsonSerializer.Serialize(new { attemptedEmail = command.Email })
                    : JsonSerializer.Serialize(new { errorCode = AdministratorErrors.InvalidCredentials.Code }),
                CorrelationId: null,
                IpAddress: null,
                UserAgent: null,
                now));

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<AdministratorLoginResult>(AdministratorErrors.InvalidCredentials);
        }

        if (!administrator.IsActive)
        {
            auditWriter.Write(new AuditEntry(
                Guid.CreateVersion7(),
                administrator.Id.Value,
                AuditActions.AdministratorLoginFailed,
                "Administrator",
                administrator.Id.Value.ToString(),
                BeforeData: null,
                AfterData: JsonSerializer.Serialize(new { errorCode = AdministratorErrors.Inactive.Code }),
                CorrelationId: null,
                IpAddress: null,
                UserAgent: null,
                now));

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<AdministratorLoginResult>(AdministratorErrors.Inactive);
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
}
