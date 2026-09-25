using EcoBilling.Control.Application.Provisioning.ResetDirectorPassword;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Domain.Provisioning;
using EcoBilling.Control.UnitTests.TestDoubles;

namespace EcoBilling.Control.UnitTests.Provisioning.ResetDirectorPassword;

public sealed class ResetDirectorPasswordHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
    private static readonly AdministratorId Caller = AdministratorId.New();

    private sealed record Fixture(
        FakeDistrictRepository Districts,
        FakeProvisioningOperationRepository Operations,
        FakeDistrictClient DistrictClient,
        FakeAuditWriter AuditWriter,
        FakeUnitOfWork UnitOfWork,
        ResetDirectorPasswordHandler Handler);

    private static Fixture NewFixture()
    {
        var districts = new FakeDistrictRepository();
        var operations = new FakeProvisioningOperationRepository();
        var districtClient = new FakeDistrictClient();
        var auditWriter = new FakeAuditWriter();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new ResetDirectorPasswordHandler(districts, operations, districtClient, auditWriter, unitOfWork, new FixedClock(Now));

        return new Fixture(districts, operations, districtClient, auditWriter, unitOfWork, handler);
    }

    private static District NewActiveDistrict(FakeDistrictRepository repository)
    {
        var district = District.Create(
            DistrictId.New(),
            DistrictCode.Create("BISHKEK-01").Value,
            "Bishkek district",
            TrustedApiUrl.Create("https://district-01.example.com/api").Value,
            Now).Value;
        district.Activate(Now);
        repository.Seed(district);

        return district;
    }

    [Fact]
    public async Task HandleAsync_ReturnsNotFound_ForAnUnknownDistrict()
    {
        var fixture = NewFixture();

        var result = await fixture.Handler.HandleAsync(
            new ResetDirectorPasswordCommand(DistrictId.New(), "director@example.com", Caller), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.not_found", result.Error.Code);
        Assert.Empty(fixture.DistrictClient.ResetDirectorPasswordCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task HandleAsync_ReturnsValidationFailed_WhenDirectorEmailIsMissing(string? email)
    {
        var fixture = NewFixture();
        var district = NewActiveDistrict(fixture.Districts);

        var result = await fixture.Handler.HandleAsync(
            new ResetDirectorPasswordCommand(district.Id, email, Caller), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation.failed", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_OnSuccess_CompletesTheOperation_AndAuditsIt()
    {
        var fixture = NewFixture();
        var district = NewActiveDistrict(fixture.Districts);

        var result = await fixture.Handler.HandleAsync(
            new ResetDirectorPasswordCommand(district.Id, "director@example.com", Caller), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var operation = fixture.Operations.Added[0];
        Assert.Equal(ProvisioningStatus.Completed, operation.Status);

        var entry = Assert.Single(fixture.AuditWriter.Entries);
        Assert.Equal("provisioning.password_reset_succeeded", entry.Action);
        Assert.Equal(Caller.Value, entry.AdministratorId);
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount);

        var call = Assert.Single(fixture.DistrictClient.ResetDirectorPasswordCalls);
        Assert.Equal("director@example.com", call.DirectorEmail);
        Assert.Equal(operation.IdempotencyKey, call.IdempotencyKey);

        // The audit trail records which director's password was reset -- without this,
        // an administrator reading the audit log would see only "a reset happened."
        Assert.Contains("director@example.com", entry.AfterData!);
    }

    [Fact]
    public async Task HandleAsync_WhenTheDistrictClientFails_FailsTheOperation_AndAuditsTheFailure()
    {
        var fixture = NewFixture();
        var district = NewActiveDistrict(fixture.Districts);
        fixture.DistrictClient.ResetDirectorPasswordResult = Result.Failure(ProvisioningOperationErrors.DirectorNotFound);

        var result = await fixture.Handler.HandleAsync(
            new ResetDirectorPasswordCommand(district.Id, "unknown@example.com", Caller), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("director.not_found", result.Error.Code);
        Assert.Equal(ProvisioningStatus.Failed, fixture.Operations.Added[0].Status);

        var entry = Assert.Single(fixture.AuditWriter.Entries);
        Assert.Equal("provisioning.password_reset_failed", entry.Action);
    }

    [Fact]
    public async Task HandleAsync_OnRetryAfterAFailure_ReusesTheSamePendingOperationAndIdempotencyKey()
    {
        var fixture = NewFixture();
        var district = NewActiveDistrict(fixture.Districts);
        fixture.DistrictClient.ResetDirectorPasswordResult = Result.Failure(ProvisioningOperationErrors.DistrictUnavailable);

        await fixture.Handler.HandleAsync(
            new ResetDirectorPasswordCommand(district.Id, "director@example.com", Caller), CancellationToken.None);

        var firstOperationId = fixture.Operations.Added[0].Id;
        var firstIdempotencyKey = fixture.Operations.Added[0].IdempotencyKey;
        fixture.DistrictClient.ResetDirectorPasswordResult = Result.Success();

        var result = await fixture.Handler.HandleAsync(
            new ResetDirectorPasswordCommand(district.Id, "director@example.com", Caller), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(firstOperationId, result.Value.OperationId);
        Assert.Single(fixture.Operations.Added); // no second operation.
        Assert.Equal(firstIdempotencyKey, fixture.DistrictClient.ResetDirectorPasswordCalls[1].IdempotencyKey);
    }

    [Fact]
    public async Task HandleAsync_WhenTheLatestOperationAlreadyCompleted_StartsAFreshOperation_UnlikeCreateDirector()
    {
        // The key behavioral difference from CreateDirector (Stage 8, section 9.2):
        // password resets are legitimately repeatable, so a prior Completed reset must
        // not block a new one.
        var fixture = NewFixture();
        var district = NewActiveDistrict(fixture.Districts);

        await fixture.Handler.HandleAsync(
            new ResetDirectorPasswordCommand(district.Id, "director@example.com", Caller), CancellationToken.None);
        var firstOperation = fixture.Operations.Added[0];
        Assert.Equal(ProvisioningStatus.Completed, firstOperation.Status);

        var result = await fixture.Handler.HandleAsync(
            new ResetDirectorPasswordCommand(district.Id, "director@example.com", Caller), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, fixture.Operations.Added.Count); // a genuinely new operation, not a rejection.
        Assert.NotEqual(firstOperation.Id, result.Value.OperationId);
        Assert.NotEqual(firstOperation.IdempotencyKey, fixture.DistrictClient.ResetDirectorPasswordCalls[1].IdempotencyKey);
    }
}
