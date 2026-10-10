using System.Text.Json;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Auditing;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.Application.Administrators.ResetAdministratorPassword;

/// <summary>
/// Resets a system administrator password only from the trusted provisioning process,
/// revokes every active refresh-token session, and records the operation in the audit trail.
/// </summary>
public sealed class ResetAdministratorPasswordHandler(
    IAdministratorRepository administrators,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher passwordHasher,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<ResetAdministratorPasswordCommand, ResetAdministratorPasswordResult>
{
    public async Task<Result<ResetAdministratorPasswordResult>> HandleAsync(
        ResetAdministratorPasswordCommand command,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var emailResult = AdministratorEmail.Create(command.Email);

        if (emailResult.IsFailure)
        {
            return await FailAsync(command.Email, emailResult.Error, now, cancellationToken);
        }

        var passwordResult = PlainTextPassword.Create(command.NewPassword);

        if (passwordResult.IsFailure)
        {
            return await FailAsync(command.Email, passwordResult.Error, now, cancellationToken);
        }

        var administrator = await administrators.GetByNormalizedEmailAsync(
            emailResult.Value.Normalized,
            cancellationToken);

        if (administrator is null)
        {
            return await FailAsync(command.Email, AdministratorErrors.NotFound, now, cancellationToken);
        }

        administrator.ResetPassword(passwordHasher.Hash(passwordResult.Value.Reveal()), now);

        var activeTokens = await refreshTokens.GetActiveByAdministratorIdAsync(
            administrator.Id,
            now,
            cancellationToken);

        foreach (var token in activeTokens)
        {
            token.Revoke(now);
        }

        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            AdministratorId: null,
            AuditActions.AdministratorPasswordReset,
            "Administrator",
            administrator.Id.Value.ToString(),
            BeforeData: null,
            AfterData: JsonSerializer.Serialize(new { revokedSessionCount = activeTokens.Count }),
            CorrelationId: null,
            IpAddress: null,
            UserAgent: null,
            now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new ResetAdministratorPasswordResult(administrator.Id, administrator.NormalizedEmail));
    }

    private async Task<Result<ResetAdministratorPasswordResult>> FailAsync(
        string? attemptedEmail,
        Error error,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            AdministratorId: null,
            AuditActions.AdministratorPasswordResetFailed,
            "Administrator",
            EntityId: null,
            BeforeData: null,
            AfterData: JsonSerializer.Serialize(new { attemptedEmail, errorCode = error.Code }),
            CorrelationId: null,
            IpAddress: null,
            UserAgent: null,
            now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Failure<ResetAdministratorPasswordResult>(error);
    }
}
