using System.Text.Json;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Auditing;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Domain.Provisioning;

namespace EcoBilling.Control.Application.Provisioning.ResetDirectorPassword;

/// <summary>
/// Resets a director's password via the district's protected internal API. Unlike
/// CreateDirector, a password reset is legitimately repeatable: a prior
/// <see cref="ProvisioningStatus.Completed"/> reset does not block a new one -- it is
/// only a Pending/Failed attempt that gets resumed with its existing idempotency key
/// (Stage 8, section 9.2), since that is the same in-flight attempt, not a new request.
/// Success and every failure are audited; no credential ever appears in the audit trail,
/// the command, or this handler (section 9.4/9.5).
/// </summary>
public sealed class ResetDirectorPasswordHandler(
    IDistrictRepository districts,
    IProvisioningOperationRepository operations,
    IDistrictClient districtClient,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<ResetDirectorPasswordCommand, ResetDirectorPasswordResult>
{
    public async Task<Result<ResetDirectorPasswordResult>> HandleAsync(
        ResetDirectorPasswordCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var district = await districts.GetByIdAsync(command.DistrictId, cancellationToken);

        if (district is null)
        {
            return await FailWithoutOperationAsync(command, DistrictErrors.NotFound, now, cancellationToken);
        }

        if (!district.IsActive)
        {
            return await FailWithoutOperationAsync(command, DistrictErrors.Inactive, now, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(command.DirectorEmail))
        {
            return await FailWithoutOperationAsync(command, ProvisioningOperationErrors.ValidationFailed, now, cancellationToken);
        }

        var existing = await operations.GetLatestAsync(district.Id, ProvisioningOperationType.PasswordReset, cancellationToken);

        ProvisioningOperation operation;

        if (existing is { Status: ProvisioningStatus.Pending or ProvisioningStatus.Failed })
        {
            operation = existing;
        }
        else
        {
            // Either no prior reset exists, or the latest one already completed --
            // either way this is a legitimate new reset request, not a retry.
            operation = ProvisioningOperation.Create(
                ProvisioningOperationId.New(), district.Id, ProvisioningOperationType.PasswordReset, now);
            operations.Add(operation);
        }

        operation.RecordAttemptStarted(now);

        var clientResult = await districtClient.ResetDirectorPasswordAsync(
            district, command.DirectorEmail, operation.IdempotencyKey, cancellationToken);

        if (clientResult.IsFailure)
        {
            return await FailAsync(command, operation, clientResult.Error, now, cancellationToken);
        }

        operation.Complete(now);

        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            command.CallingAdministratorId.Value,
            AuditActions.ProvisioningPasswordResetSucceeded,
            "ProvisioningOperation",
            operation.Id.Value.ToString(),
            BeforeData: null,
            AfterData: JsonSerializer.Serialize(new
            {
                operationId = operation.Id.Value,
                status = operation.Status.ToString(),
                directorEmail = command.DirectorEmail,
            }),
            CorrelationId: null,
            IpAddress: null,
            UserAgent: null,
            now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new ResetDirectorPasswordResult(operation.Id));
    }

    /// <summary>District not found or inactive -- no <see cref="ProvisioningOperation"/> is ever created for these.</summary>
    private async Task<Result<ResetDirectorPasswordResult>> FailWithoutOperationAsync(
        ResetDirectorPasswordCommand command, Error error, DateTimeOffset now, CancellationToken cancellationToken)
    {
        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            command.CallingAdministratorId.Value,
            AuditActions.ProvisioningPasswordResetFailed,
            "ProvisioningOperation",
            EntityId: null,
            BeforeData: null,
            AfterData: JsonSerializer.Serialize(new { attemptedDirectorEmail = command.DirectorEmail, errorCode = error.Code }),
            CorrelationId: null,
            IpAddress: null,
            UserAgent: null,
            now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Failure<ResetDirectorPasswordResult>(error);
    }

    private async Task<Result<ResetDirectorPasswordResult>> FailAsync(
        ResetDirectorPasswordCommand command, ProvisioningOperation operation, Error error, DateTimeOffset now, CancellationToken cancellationToken)
    {
        operation.Fail(error.Code, now);

        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            command.CallingAdministratorId.Value,
            AuditActions.ProvisioningPasswordResetFailed,
            "ProvisioningOperation",
            operation.Id.Value.ToString(),
            BeforeData: null,
            AfterData: JsonSerializer.Serialize(new { operationId = operation.Id.Value, errorCode = error.Code }),
            CorrelationId: null,
            IpAddress: null,
            UserAgent: null,
            now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Failure<ResetDirectorPasswordResult>(error);
    }
}
