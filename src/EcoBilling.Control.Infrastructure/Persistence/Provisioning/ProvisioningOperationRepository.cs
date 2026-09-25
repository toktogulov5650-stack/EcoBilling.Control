using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Domain.Provisioning;
using Microsoft.EntityFrameworkCore;

namespace EcoBilling.Control.Infrastructure.Persistence.Provisioning;

/// <summary>PostgreSQL-backed <see cref="IProvisioningOperationRepository"/>.</summary>
public sealed class ProvisioningOperationRepository(ControlDbContext dbContext) : IProvisioningOperationRepository
{
    public Task<ProvisioningOperation?> GetLatestAsync(
        DistrictId districtId, ProvisioningOperationType operationType, CancellationToken cancellationToken) =>
        dbContext.ProvisioningOperations
            // Tracked: CreateDirector/ResetDirectorPassword call RecordAttemptStarted/
            // Complete/Fail on what this returns.
            .Where(o => o.DistrictId == districtId && o.OperationType == operationType)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<ProvisioningOperation?> GetByIdAsync(ProvisioningOperationId id, CancellationToken cancellationToken) =>
        dbContext.ProvisioningOperations
            .AsNoTracking() // Read-only admin status view.
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

    public void Add(ProvisioningOperation operation) => dbContext.ProvisioningOperations.Add(operation);
}
