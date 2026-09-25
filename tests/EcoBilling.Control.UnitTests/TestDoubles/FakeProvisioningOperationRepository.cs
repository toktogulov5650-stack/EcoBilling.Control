using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Domain.Provisioning;

namespace EcoBilling.Control.UnitTests.TestDoubles;

/// <summary>In-memory <see cref="IProvisioningOperationRepository"/>, matching the reasoning behind every other Fake in this folder.</summary>
internal sealed class FakeProvisioningOperationRepository : IProvisioningOperationRepository
{
    private readonly Dictionary<ProvisioningOperationId, ProvisioningOperation> _byId = [];

    public List<ProvisioningOperation> Added { get; } = [];

    public void Seed(ProvisioningOperation operation) => _byId[operation.Id] = operation;

    public Task<ProvisioningOperation?> GetLatestAsync(
        DistrictId districtId, ProvisioningOperationType operationType, CancellationToken cancellationToken)
    {
        var latest = _byId.Values
            .Where(o => o.DistrictId == districtId && o.OperationType == operationType)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefault();

        return Task.FromResult(latest);
    }

    public Task<ProvisioningOperation?> GetByIdAsync(ProvisioningOperationId id, CancellationToken cancellationToken) =>
        Task.FromResult(_byId.GetValueOrDefault(id));

    public void Add(ProvisioningOperation operation)
    {
        Added.Add(operation);
        Seed(operation);
    }
}
