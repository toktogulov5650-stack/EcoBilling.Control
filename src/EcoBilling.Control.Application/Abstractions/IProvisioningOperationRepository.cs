using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Domain.Provisioning;

namespace EcoBilling.Control.Application.Abstractions;

/// <summary>Access to provisioning-operation history.</summary>
public interface IProvisioningOperationRepository
{
    /// <summary>
    /// The most recent operation of this type for this district, tracked, or
    /// <c>null</c> if none exists yet. CreateDirector and ResetDirectorPassword (Stage 8,
    /// section 9.2) use this to decide whether to reuse a Pending/Failed operation's
    /// idempotency key as a retry, or start a fresh one -- the caller, not this method,
    /// decides what a <c>Completed</c> result means for its own operation type (reject
    /// as already-done for director creation; allow a new one for the repeatable
    /// password reset).
    /// </summary>
    Task<ProvisioningOperation?> GetLatestAsync(
        DistrictId districtId, ProvisioningOperationType operationType, CancellationToken cancellationToken);

    /// <summary>Looks up an operation by id, for the admin-facing status endpoint. No-tracking: read-only.</summary>
    Task<ProvisioningOperation?> GetByIdAsync(ProvisioningOperationId id, CancellationToken cancellationToken);

    /// <summary>Stages a new operation for insertion. Nothing is persisted until <see cref="IUnitOfWork.SaveChangesAsync"/> is called.</summary>
    void Add(ProvisioningOperation operation);
}
