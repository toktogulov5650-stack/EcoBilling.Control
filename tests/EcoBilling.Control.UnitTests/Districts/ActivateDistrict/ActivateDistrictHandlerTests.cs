using EcoBilling.Control.Application.Districts.ActivateDistrict;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.UnitTests.TestDoubles;

namespace EcoBilling.Control.UnitTests.Districts.ActivateDistrict;

public sealed class ActivateDistrictHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(1);

    private static District NewDistrict() =>
        District.Create(
            DistrictId.New(),
            DistrictCode.Create("BISHKEK-01").Value,
            "Bishkek district",
            TrustedApiUrl.Create("https://district-01.example.com/api").Value,
            Now).Value;

    [Fact]
    public async Task HandleAsync_ReturnsNotFoundForAnUnknownDistrict()
    {
        var repository = new FakeDistrictRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new ActivateDistrictHandler(repository, unitOfWork, new FixedClock(Later));

        var result = await handler.HandleAsync(new ActivateDistrictCommand(DistrictId.New()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_ActivatesAnInactiveDistrict()
    {
        var district = NewDistrict();
        var repository = new FakeDistrictRepository();
        repository.Seed(district);
        var unitOfWork = new FakeUnitOfWork();
        var handler = new ActivateDistrictHandler(repository, unitOfWork, new FixedClock(Later));

        var result = await handler.HandleAsync(new ActivateDistrictCommand(district.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(district.IsActive);
        Assert.Equal(Later, district.ActivatedAt);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_OnAnAlreadyActiveDistrict_SucceedsAsANoOp_WithoutWriting()
    {
        // Stage 1 decision: Domain's own invariant (Activate on an active district is an
        // error) stays strict; this handler simply never reaches that path on a repeat
        // call, so a retried "activate" is idempotent for the admin caller.
        var district = NewDistrict();
        district.Activate(Now);
        var originalActivatedAt = district.ActivatedAt;
        var repository = new FakeDistrictRepository();
        repository.Seed(district);
        var unitOfWork = new FakeUnitOfWork();
        var handler = new ActivateDistrictHandler(repository, unitOfWork, new FixedClock(Later));

        var result = await handler.HandleAsync(new ActivateDistrictCommand(district.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(originalActivatedAt, district.ActivatedAt); // unchanged -- Activate() was never called again
        Assert.Equal(0, unitOfWork.SaveChangesCallCount); // no wasted write, no spurious audit entry later
    }
}
