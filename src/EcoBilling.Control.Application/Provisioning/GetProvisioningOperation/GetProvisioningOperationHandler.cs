using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Provisioning;

namespace EcoBilling.Control.Application.Provisioning.GetProvisioningOperation;

/// <summary>Reads one provisioning operation's status for the admin view. Never writes -- viewing is not itself an audited action.</summary>
public sealed class GetProvisioningOperationHandler(IProvisioningOperationRepository operations)
    : IQueryHandler<GetProvisioningOperationQuery, GetProvisioningOperationResult>
{
    public async Task<Result<GetProvisioningOperationResult>> HandleAsync(
        GetProvisioningOperationQuery query, CancellationToken cancellationToken)
    {
        var operation = await operations.GetByIdAsync(query.OperationId, cancellationToken);

        if (operation is null)
        {
            return Result.Failure<GetProvisioningOperationResult>(ProvisioningOperationErrors.NotFound);
        }

        return Result.Success(new GetProvisioningOperationResult(
            operation.Id,
            operation.DistrictId,
            operation.OperationType,
            operation.Status,
            operation.AttemptCount,
            operation.CreatedAt,
            operation.StartedAt,
            operation.CompletedAt,
            operation.FailedAt,
            operation.LastErrorCode));
    }
}
