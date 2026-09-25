using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Domain.Provisioning;

namespace EcoBilling.Control.Application.Provisioning.GetProvisioningOperation;

public sealed record GetProvisioningOperationResult(
    ProvisioningOperationId OperationId,
    DistrictId DistrictId,
    ProvisioningOperationType OperationType,
    ProvisioningStatus Status,
    int AttemptCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FailedAt,
    string? LastErrorCode);
