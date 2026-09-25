using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Provisioning;

namespace EcoBilling.Control.Application.Provisioning.GetProvisioningOperation;

public sealed record GetProvisioningOperationQuery(ProvisioningOperationId OperationId) : IQuery<GetProvisioningOperationResult>;
