using EcoBilling.Control.Domain.Provisioning;

namespace EcoBilling.Control.Application.Provisioning.CreateDirector;

public sealed record CreateDirectorResult(ProvisioningOperationId OperationId, string DirectorId);
