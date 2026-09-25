using System.Text.Json;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Auditing;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Domain.Provisioning;

namespace EcoBilling.Control.Application.Provisioning.CreateDirector;

/// <summary>
/// Creates a district's first director via its protected internal API. Director creation
/// is a once-only operation per district (architecture doc, section 17.2: "первый
/// директор") -- a prior <see cref="ProvisioningStatus.Completed"/> attempt is rejected
/// as <c>director.already_exists</c> rather than retried, but a Pending/Failed attempt is
/// resumed using its existing idempotency key (Stage 8, section 9.2). Success and every
/// failure are audited (same policy as CreateDistrict, Stage 7); no director credential
/// ever appears in the audit trail, the command, or this handler (section 9.4).
/// </summary>
public sealed class CreateDirectorHandler(
    IDistrictRepository districts,
    IProvisioningOperationRepository operations,
    IDistrictClient districtClient,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CreateDirectorCommand, CreateDirectorResult>
{
    public async Task<Result<CreateDirectorResult>> HandleAsync(CreateDirectorCommand command, CancellationToken cancellationToken)
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

        if (string.IsNullOrWhiteSpace(command.FullName) || string.IsNullOrWhiteSpace(command.Email))
        {
            return await FailWithoutOperationAsync(command, ProvisioningOperationErrors.ValidationFailed, now, cancellationToken);
        }

        var existing = await operations.GetLatestAsync(district.Id, ProvisioningOperationType.DirectorCreation, cancellationToken);

        if (existing is { Status: ProvisioningStatus.Completed })
        {
            return await FailAsync(command, existing, ProvisioningOperationErrors.DirectorAlreadyExists, now, cancellationToken);
        }

        ProvisioningOperation operation;

        if (existing is { Status: ProvisioningStatus.Pending or ProvisioningStatus.Failed })
        {
            operation = existing;
        }
        else
        {
            operation = ProvisioningOperation.Create(
                ProvisioningOperationId.New(), district.Id, ProvisioningOperationType.DirectorCreation, now);
            operations.Add(operation);
        }

        operation.RecordAttemptStarted(now);

        var clientResult = await districtClient.CreateDirectorAsync(
            district, new CreateDirectorRequest(command.FullName, command.Email), operation.IdempotencyKey, cancellationToken);

        if (clientResult.IsFailure)
        {
            return await FailAsync(command, operation, clientResult.Error, now, cancellationToken);
        }

        operation.Complete(now);

        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            command.CallingAdministratorId.Value,
            AuditActions.ProvisioningDirectorCreationSucceeded,
            "ProvisioningOperation",
            operation.Id.Value.ToString(),
            BeforeData: null,
            AfterData: JsonSerializer.Serialize(new
            {
                operationId = operation.Id.Value,
                status = operation.Status.ToString(),
                directorId = clientResult.Value.DirectorId,
                email = command.Email,
            }),
            CorrelationId: null,
            IpAddress: null,
            UserAgent: null,
            now));

        if (!await TrySaveChangesAsync(cancellationToken))
        {
            return Result.Failure<CreateDirectorResult>(ProvisioningOperationErrors.ConcurrentConflict);
        }

        return Result.Success(new CreateDirectorResult(operation.Id, clientResult.Value.DirectorId));
    }

    /// <summary>
    /// Uses <see cref="IUnitOfWork.SaveChangesOrConflictAsync"/> so a losing race against
    /// the partial unique index on <c>ProvisioningOperations(DistrictId)</c> (Stage 9) --
    /// two concurrent CreateDirector requests for the same district both inserting a new
    /// operation before either committed -- surfaces as a normal failed <see cref="Result"/>
    /// instead of an unhandled exception. The losing request's own retry will see the
    /// now-committed operation via GetLatestAsync and reuse it correctly.
    /// </summary>
    private async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken) =>
        (await unitOfWork.SaveChangesOrConflictAsync(cancellationToken)).IsSuccess;

    /// <summary>District not found or inactive -- no <see cref="ProvisioningOperation"/> is ever created for these.</summary>
    private async Task<Result<CreateDirectorResult>> FailWithoutOperationAsync(
        CreateDirectorCommand command, Error error, DateTimeOffset now, CancellationToken cancellationToken)
    {
        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            command.CallingAdministratorId.Value,
            AuditActions.ProvisioningDirectorCreationFailed,
            "ProvisioningOperation",
            EntityId: null,
            BeforeData: null,
            AfterData: JsonSerializer.Serialize(new { attemptedEmail = command.Email, errorCode = error.Code }),
            CorrelationId: null,
            IpAddress: null,
            UserAgent: null,
            now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Failure<CreateDirectorResult>(error);
    }

    private async Task<Result<CreateDirectorResult>> FailAsync(
        CreateDirectorCommand command, ProvisioningOperation operation, Error error, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Only actually mutates the operation when it isn't already Completed (the
        // director.already_exists early-return calls this on a Completed operation
        // purely to reuse the audit-and-save plumbing; Fail() would reject that
        // transition, so it is not called in that branch -- see the caller).
        if (operation.Status != ProvisioningStatus.Completed)
        {
            operation.Fail(error.Code, now);
        }

        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            command.CallingAdministratorId.Value,
            AuditActions.ProvisioningDirectorCreationFailed,
            "ProvisioningOperation",
            operation.Id.Value.ToString(),
            BeforeData: null,
            AfterData: JsonSerializer.Serialize(new { operationId = operation.Id.Value, errorCode = error.Code }),
            CorrelationId: null,
            IpAddress: null,
            UserAgent: null,
            now));

        if (!await TrySaveChangesAsync(cancellationToken))
        {
            return Result.Failure<CreateDirectorResult>(ProvisioningOperationErrors.ConcurrentConflict);
        }

        return Result.Failure<CreateDirectorResult>(error);
    }
}
