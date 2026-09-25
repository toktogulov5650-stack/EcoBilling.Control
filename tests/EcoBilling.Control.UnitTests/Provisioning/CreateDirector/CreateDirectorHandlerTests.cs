using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Provisioning.CreateDirector;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Domain.Provisioning;
using EcoBilling.Control.UnitTests.TestDoubles;

namespace EcoBilling.Control.UnitTests.Provisioning.CreateDirector;

public sealed class CreateDirectorHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
    private static readonly AdministratorId Caller = AdministratorId.New();

    private sealed record Fixture(
        FakeDistrictRepository Districts,
        FakeProvisioningOperationRepository Operations,
        FakeDistrictClient DistrictClient,
        FakeAuditWriter AuditWriter,
        FakeUnitOfWork UnitOfWork,
        CreateDirectorHandler Handler);

    private static Fixture NewFixture()
    {
        var districts = new FakeDistrictRepository();
        var operations = new FakeProvisioningOperationRepository();
        var districtClient = new FakeDistrictClient();
        var auditWriter = new FakeAuditWriter();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new CreateDirectorHandler(districts, operations, districtClient, auditWriter, unitOfWork, new FixedClock(Now));

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
    public async Task HandleAsync_ReturnsNotFound_ForAnUnknownDistrict_AndNeverCallsTheDistrictClient()
    {
        var fixture = NewFixture();

        var result = await fixture.Handler.HandleAsync(
            new CreateDirectorCommand(DistrictId.New(), "Director", "director@example.com", Caller), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.not_found", result.Error.Code);
        Assert.Empty(fixture.DistrictClient.CreateDirectorCalls);
        Assert.Empty(fixture.Operations.Added);
    }

    [Fact]
    public async Task HandleAsync_ReturnsInactive_ForAnInactiveDistrict()
    {
        var fixture = NewFixture();
        var district = District.Create(
            DistrictId.New(), DistrictCode.Create("BISHKEK-01").Value, "Bishkek district",
            TrustedApiUrl.Create("https://district-01.example.com/api").Value, Now).Value; // never activated
        fixture.Districts.Seed(district);

        var result = await fixture.Handler.HandleAsync(
            new CreateDirectorCommand(district.Id, "Director", "director@example.com", Caller), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.inactive", result.Error.Code);
        Assert.Empty(fixture.DistrictClient.CreateDirectorCalls);
    }

    [Theory]
    [InlineData(null, "director@example.com")]
    [InlineData("   ", "director@example.com")]
    [InlineData("Director", null)]
    [InlineData("Director", "   ")]
    public async Task HandleAsync_ReturnsValidationFailed_WhenFullNameOrEmailIsMissing(string? fullName, string? email)
    {
        var fixture = NewFixture();
        var district = NewActiveDistrict(fixture.Districts);

        var result = await fixture.Handler.HandleAsync(
            new CreateDirectorCommand(district.Id, fullName, email, Caller), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation.failed", result.Error.Code);
        Assert.Empty(fixture.DistrictClient.CreateDirectorCalls);
    }

    [Fact]
    public async Task HandleAsync_OnFirstAttempt_CreatesANewOperation_AndCallsTheDistrictClientWithItsIdempotencyKey()
    {
        var fixture = NewFixture();
        var district = NewActiveDistrict(fixture.Districts);

        var result = await fixture.Handler.HandleAsync(
            new CreateDirectorCommand(district.Id, "Director", "director@example.com", Caller), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("director-id", result.Value.DirectorId);
        Assert.Single(fixture.Operations.Added);

        var operation = fixture.Operations.Added[0];
        Assert.Equal(ProvisioningStatus.Completed, operation.Status);
        Assert.Equal(1, operation.AttemptCount);

        var call = Assert.Single(fixture.DistrictClient.CreateDirectorCalls);
        Assert.Equal(operation.IdempotencyKey, call.IdempotencyKey);
        Assert.Equal("Director", call.Request.FullName);
        Assert.Equal("director@example.com", call.Request.Email);
    }

    [Fact]
    public async Task HandleAsync_OnSuccess_AuditsWithTheCallingAdministrator_AndCommitsExactlyOnce()
    {
        var fixture = NewFixture();
        var district = NewActiveDistrict(fixture.Districts);

        await fixture.Handler.HandleAsync(
            new CreateDirectorCommand(district.Id, "Director", "director@example.com", Caller), CancellationToken.None);

        var entry = Assert.Single(fixture.AuditWriter.Entries);
        Assert.Equal("provisioning.director_creation_succeeded", entry.Action);
        Assert.Equal(Caller.Value, entry.AdministratorId);
        Assert.Equal("ProvisioningOperation", entry.EntityType);
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount);

        // The audit trail records which director resulted from this call -- without
        // this, an administrator reading the audit log would see only "a director was
        // created," not which one.
        Assert.Contains("director-id", entry.AfterData!);
        Assert.Contains("director@example.com", entry.AfterData!);
    }

    [Fact]
    public async Task HandleAsync_WhenTheDistrictClientFails_FailsTheOperation_AndAuditsTheFailure()
    {
        var fixture = NewFixture();
        var district = NewActiveDistrict(fixture.Districts);
        fixture.DistrictClient.CreateDirectorResult =
            Result.Failure<DirectorCreationAcknowledged>(ProvisioningOperationErrors.DistrictUnavailable);

        var result = await fixture.Handler.HandleAsync(
            new CreateDirectorCommand(district.Id, "Director", "director@example.com", Caller), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.unavailable", result.Error.Code);

        var operation = fixture.Operations.Added[0];
        Assert.Equal(ProvisioningStatus.Failed, operation.Status);
        Assert.Equal("district.unavailable", operation.LastErrorCode);

        var entry = Assert.Single(fixture.AuditWriter.Entries);
        Assert.Equal("provisioning.director_creation_failed", entry.Action);
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_OnRetryAfterAFailure_ReusesTheSamePendingOperationAndIdempotencyKey()
    {
        var fixture = NewFixture();
        var district = NewActiveDistrict(fixture.Districts);
        fixture.DistrictClient.CreateDirectorResult =
            Result.Failure<DirectorCreationAcknowledged>(ProvisioningOperationErrors.DistrictUnavailable);

        await fixture.Handler.HandleAsync(
            new CreateDirectorCommand(district.Id, "Director", "director@example.com", Caller), CancellationToken.None);

        var firstOperationId = fixture.Operations.Added[0].Id;
        var firstIdempotencyKey = fixture.Operations.Added[0].IdempotencyKey;

        // Retry: this time the district accepts it.
        fixture.DistrictClient.CreateDirectorResult = Result.Success(new DirectorCreationAcknowledged("director-id"));

        var result = await fixture.Handler.HandleAsync(
            new CreateDirectorCommand(district.Id, "Director", "director@example.com", Caller), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(firstOperationId, result.Value.OperationId); // same operation, not a new one.
        Assert.Single(fixture.Operations.Added); // no second operation was ever created.
        Assert.Equal(2, fixture.DistrictClient.CreateDirectorCalls.Count);
        Assert.Equal(firstIdempotencyKey, fixture.DistrictClient.CreateDirectorCalls[1].IdempotencyKey);
        Assert.Equal(2, fixture.Operations.Added[0].AttemptCount);
    }

    [Fact]
    public async Task HandleAsync_WhenACompletedOperationAlreadyExists_RejectsAsAlreadyExists_WithoutCallingTheDistrictClientAgain()
    {
        var fixture = NewFixture();
        var district = NewActiveDistrict(fixture.Districts);

        await fixture.Handler.HandleAsync(
            new CreateDirectorCommand(district.Id, "Director", "director@example.com", Caller), CancellationToken.None);
        Assert.Single(fixture.DistrictClient.CreateDirectorCalls); // sanity: the first call did happen.

        var result = await fixture.Handler.HandleAsync(
            new CreateDirectorCommand(district.Id, "Director", "director@example.com", Caller), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("director.already_exists", result.Error.Code);
        Assert.Single(fixture.DistrictClient.CreateDirectorCalls); // still just the one call -- rejected before reaching the client.
        Assert.Single(fixture.Operations.Added); // still just the one operation ever created.

        Assert.Equal(2, fixture.AuditWriter.Entries.Count);
        Assert.Equal("provisioning.director_creation_failed", fixture.AuditWriter.Entries[1].Action);
    }

    [Fact]
    public async Task HandleAsync_WhenSaveChangesLosesAConcurrencyRace_OnTheSuccessPath_ReturnsConcurrentConflict()
    {
        // Simulates the actual race from Stage 9, section 1: this request's district
        // call succeeded, but by the time it tries to commit, a concurrent request for
        // the same district has already committed its own DirectorCreation operation --
        // SaveChangesOrConflictAsync (Stage 9) turns that into a failed Result, which
        // the handler must surface as its own failed Result, not treat as success.
        var fixture = NewFixture();
        var district = NewActiveDistrict(fixture.Districts);
        fixture.UnitOfWork.SaveChangesOrConflictResult = Result.Failure(UnitOfWorkErrors.ConcurrencyConflict);

        var result = await fixture.Handler.HandleAsync(
            new CreateDirectorCommand(district.Id, "Director", "director@example.com", Caller), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("provisioning.concurrent_conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenSaveChangesLosesAConcurrencyRace_OnTheFailurePath_ReturnsConcurrentConflict_NotTheOriginalError()
    {
        var fixture = NewFixture();
        var district = NewActiveDistrict(fixture.Districts);
        fixture.DistrictClient.CreateDirectorResult =
            Result.Failure<DirectorCreationAcknowledged>(ProvisioningOperationErrors.DistrictUnavailable);
        fixture.UnitOfWork.SaveChangesOrConflictResult = Result.Failure(UnitOfWorkErrors.ConcurrencyConflict);

        var result = await fixture.Handler.HandleAsync(
            new CreateDirectorCommand(district.Id, "Director", "director@example.com", Caller), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("provisioning.concurrent_conflict", result.Error.Code); // not district.unavailable.
    }
}
