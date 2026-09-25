using EcoBilling.Control.Application.Abstractions;
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
/// this handler still distinguishes <see cref="AdministratorErrors.Inactive"/> so a
/// future audit trail (Stage 7) can record which case actually happened, even though
/// the client-visible response never does.
///
/// NOTE: this scenario ships with none of its three stated protections yet (doc,
/// section 19.2: "Rate limit, lockout, audit"). Rate limiting is Stage 14; lockout is
/// Stage 1's still-undecided open question; audit is Stage 7. It must not be exposed to
/// real/public traffic until Stage 14 and the lockout policy land -- the same standing
/// caveat already on ResolveDistrict (Stage 3).
/// </remarks>
public sealed class AdministratorLoginHandler(
    IAdministratorRepository administrators,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher passwordHasher,
    IAccessTokenIssuer accessTokenIssuer,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<AdministratorLoginCommand, AdministratorLoginResult>
{
    public async Task<Result<AdministratorLoginResult>> HandleAsync(
        AdministratorLoginCommand command,
        CancellationToken cancellationToken)
    {
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
            return Result.Failure<AdministratorLoginResult>(AdministratorErrors.InvalidCredentials);
        }

        if (!administrator.IsActive)
        {
            return Result.Failure<AdministratorLoginResult>(AdministratorErrors.Inactive);
        }

        var now = clock.UtcNow;
        administrator.RecordLogin(now);

        var accessToken = accessTokenIssuer.Issue(administrator, now);
        var issued = RefreshToken.IssueNew(RefreshTokenId.New(), administrator.Id, now, RefreshTokenPolicy.Lifetime);
        refreshTokens.Add(issued.Token);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new AdministratorLoginResult(
            accessToken.Value,
            accessToken.ExpiresAt,
            issued.RawValue,
            issued.Token.ExpiresAt));
    }
}
