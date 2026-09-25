using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Domain.Provisioning;

/// <summary>
/// Tracks a single cross-service call from Control to a district's EcoBilling instance
/// (CreateDirector or ResetDirectorPassword) -- the history and idempotency anchor for
/// that call, not the call itself. <see cref="IdempotencyKey"/> is generated once, at
/// creation, and stays fixed for the operation's whole life: a retried attempt of the
/// same logical operation reuses this same instance and the same key (Stage 8, section
/// 9.2), which is what makes the key's uniqueness constraint (architecture doc, section
/// 18.1) meaningful.
/// </summary>
public sealed class ProvisioningOperation
{
    private ProvisioningOperation(
        ProvisioningOperationId id,
        DistrictId districtId,
        ProvisioningOperationType operationType,
        string idempotencyKey,
        DateTimeOffset createdAt)
    {
        Id = id;
        DistrictId = districtId;
        OperationType = operationType;
        IdempotencyKey = idempotencyKey;
        Status = ProvisioningStatus.Pending;
        AttemptCount = 0;
        CreatedAt = createdAt;
    }

    /// <summary>For EF Core materialization only (Stage 4's pattern) -- never called by application code.</summary>
    private ProvisioningOperation()
    {
    }

    public ProvisioningOperationId Id { get; }

    public DistrictId DistrictId { get; }

    public ProvisioningOperationType OperationType { get; }

    /// <summary>Sent as the <c>Idempotency-Key</c> header on every attempt of this operation.</summary>
    public string IdempotencyKey { get; } = null!;

    public ProvisioningStatus Status { get; private set; }

    /// <summary>
    /// How many times a caller has asked Control to attempt this logical operation --
    /// not the low-level HTTP retry count inside a single attempt, which the resilience
    /// pipeline (Infrastructure) handles transparently and never surfaces here.
    /// </summary>
    public int AttemptCount { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? FailedAt { get; private set; }

    public string? LastErrorCode { get; private set; }

    public static ProvisioningOperation Create(
        ProvisioningOperationId id,
        DistrictId districtId,
        ProvisioningOperationType operationType,
        DateTimeOffset now)
    {
        if (id == ProvisioningOperationId.Empty)
        {
            throw new ArgumentException("A provisioning operation id must not be empty.", nameof(id));
        }

        if (districtId == DistrictId.Empty)
        {
            throw new ArgumentException("A district id must not be empty.", nameof(districtId));
        }

        return new ProvisioningOperation(id, districtId, operationType, GenerateIdempotencyKey(), now);
    }

    /// <summary>
    /// Records that a call attempt for this operation is starting. Strict, like
    /// <c>District.Activate</c>/<c>Deactivate</c> (Stage 1): a completed operation cannot
    /// be attempted again -- the Application layer decides whether that means "reject as
    /// already done" (CreateDirector) or "start a fresh operation instead" (
    /// ResetDirectorPassword, which is legitimately repeatable), never by calling this on
    /// the completed instance.
    /// </summary>
    public Result RecordAttemptStarted(DateTimeOffset now)
    {
        if (Status == ProvisioningStatus.Completed)
        {
            return Result.Failure(ProvisioningOperationErrors.AlreadyCompleted);
        }

        StartedAt ??= now;
        AttemptCount++;
        Status = ProvisioningStatus.InProgress;

        return Result.Success();
    }

    public Result Complete(DateTimeOffset now)
    {
        if (Status == ProvisioningStatus.Completed)
        {
            return Result.Failure(ProvisioningOperationErrors.AlreadyCompleted);
        }

        Status = ProvisioningStatus.Completed;
        CompletedAt = now;

        return Result.Success();
    }

    public Result Fail(string errorCode, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        if (Status == ProvisioningStatus.Completed)
        {
            return Result.Failure(ProvisioningOperationErrors.AlreadyCompleted);
        }

        Status = ProvisioningStatus.Failed;
        LastErrorCode = errorCode;
        FailedAt = now;

        return Result.Success();
    }

    private static string GenerateIdempotencyKey() => Guid.CreateVersion7().ToString();
}
