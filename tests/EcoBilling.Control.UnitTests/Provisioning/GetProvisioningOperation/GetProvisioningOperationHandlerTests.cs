using EcoBilling.Control.Application.Provisioning.GetProvisioningOperation;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Domain.Provisioning;
using EcoBilling.Control.UnitTests.TestDoubles;

namespace EcoBilling.Control.UnitTests.Provisioning.GetProvisioningOperation;

public sealed class GetProvisioningOperationHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ReturnsTheOperation_WhenFound()
    {
        var repository = new FakeProvisioningOperationRepository();
        var operation = ProvisioningOperation.Create(
            ProvisioningOperationId.New(), DistrictId.New(), ProvisioningOperationType.DirectorCreation, Now);
        repository.Seed(operation);
        var handler = new GetProvisioningOperationHandler(repository);

        var result = await handler.HandleAsync(new GetProvisioningOperationQuery(operation.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(operation.Id, result.Value.OperationId);
        Assert.Equal(operation.DistrictId, result.Value.DistrictId);
        Assert.Equal(ProvisioningOperationType.DirectorCreation, result.Value.OperationType);
        Assert.Equal(ProvisioningStatus.Pending, result.Value.Status);
    }

    [Fact]
    public async Task HandleAsync_ReturnsNotFound_WhenMissing()
    {
        var repository = new FakeProvisioningOperationRepository();
        var handler = new GetProvisioningOperationHandler(repository);

        var result = await handler.HandleAsync(
            new GetProvisioningOperationQuery(ProvisioningOperationId.New()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("provisioning.operation_not_found", result.Error.Code);
    }
}
