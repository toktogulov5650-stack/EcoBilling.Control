using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Domain.Provisioning;

namespace EcoBilling.Control.UnitTests.Provisioning;

public sealed class ProvisioningOperationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddMinutes(5);

    private static ProvisioningOperation NewOperation(ProvisioningOperationType type = ProvisioningOperationType.DirectorCreation) =>
        ProvisioningOperation.Create(ProvisioningOperationId.New(), DistrictId.New(), type, Now);

    [Fact]
    public void Create_StartsPending_WithZeroAttempts_AndNoTimestampsBeyondCreatedAt()
    {
        var operation = NewOperation();

        Assert.Equal(ProvisioningStatus.Pending, operation.Status);
        Assert.Equal(0, operation.AttemptCount);
        Assert.Equal(Now, operation.CreatedAt);
        Assert.Null(operation.StartedAt);
        Assert.Null(operation.CompletedAt);
        Assert.Null(operation.FailedAt);
        Assert.Null(operation.LastErrorCode);
    }

    [Fact]
    public void Create_GeneratesANonEmptyIdempotencyKey()
    {
        var operation = NewOperation();

        Assert.False(string.IsNullOrWhiteSpace(operation.IdempotencyKey));
    }

    [Fact]
    public void Create_GeneratesADifferentIdempotencyKeyPerOperation()
    {
        var first = NewOperation();
        var second = NewOperation();

        Assert.NotEqual(first.IdempotencyKey, second.IdempotencyKey);
    }

    [Fact]
    public void Create_RejectsAnEmptyIdAsAProgrammingError()
    {
        Assert.Throws<ArgumentException>(() => ProvisioningOperation.Create(
            ProvisioningOperationId.Empty, DistrictId.New(), ProvisioningOperationType.DirectorCreation, Now));
    }

    [Fact]
    public void Create_RejectsAnEmptyDistrictIdAsAProgrammingError()
    {
        Assert.Throws<ArgumentException>(() => ProvisioningOperation.Create(
            ProvisioningOperationId.New(), DistrictId.Empty, ProvisioningOperationType.DirectorCreation, Now));
    }

    [Fact]
    public void RecordAttemptStarted_TransitionsToInProgress_AndSetsStartedAt()
    {
        var operation = NewOperation();

        var result = operation.RecordAttemptStarted(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProvisioningStatus.InProgress, operation.Status);
        Assert.Equal(1, operation.AttemptCount);
        Assert.Equal(Now, operation.StartedAt);
    }

    [Fact]
    public void RecordAttemptStarted_CalledAgainOnRetry_IncrementsAttemptCount_ButKeepsTheOriginalStartedAt()
    {
        var operation = NewOperation();
        operation.RecordAttemptStarted(Now);
        operation.Fail("district.unavailable", Now.AddSeconds(10));

        var result = operation.RecordAttemptStarted(Later);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, operation.AttemptCount);
        Assert.Equal(Now, operation.StartedAt); // first attempt's timestamp, not overwritten by the retry.
    }

    [Fact]
    public void RecordAttemptStarted_OnACompletedOperationIsRejected()
    {
        var operation = NewOperation();
        operation.RecordAttemptStarted(Now);
        operation.Complete(Now);

        var result = operation.RecordAttemptStarted(Later);

        Assert.True(result.IsFailure);
        Assert.Equal("provisioning.already_completed", result.Error.Code);
        Assert.Equal(1, operation.AttemptCount); // unchanged -- the rejected call never mutated anything.
    }

    [Fact]
    public void Complete_SetsStatusAndCompletedAt()
    {
        var operation = NewOperation();
        operation.RecordAttemptStarted(Now);

        var result = operation.Complete(Later);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProvisioningStatus.Completed, operation.Status);
        Assert.Equal(Later, operation.CompletedAt);
    }

    [Fact]
    public void Complete_OnAnAlreadyCompletedOperationIsRejected()
    {
        var operation = NewOperation();
        operation.RecordAttemptStarted(Now);
        operation.Complete(Now);

        var result = operation.Complete(Later);

        Assert.True(result.IsFailure);
        Assert.Equal("provisioning.already_completed", result.Error.Code);
        Assert.Equal(Now, operation.CompletedAt); // unchanged.
    }

    [Fact]
    public void Fail_SetsStatusFailedAtAndErrorCode()
    {
        var operation = NewOperation();
        operation.RecordAttemptStarted(Now);

        var result = operation.Fail("district.unavailable", Later);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProvisioningStatus.Failed, operation.Status);
        Assert.Equal(Later, operation.FailedAt);
        Assert.Equal("district.unavailable", operation.LastErrorCode);
    }

    [Fact]
    public void Fail_OnAnAlreadyCompletedOperationIsRejected()
    {
        var operation = NewOperation();
        operation.RecordAttemptStarted(Now);
        operation.Complete(Now);

        var result = operation.Fail("district.unavailable", Later);

        Assert.True(result.IsFailure);
        Assert.Equal("provisioning.already_completed", result.Error.Code);
        Assert.Null(operation.LastErrorCode); // unchanged.
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Fail_RejectsAnEmptyErrorCodeAsAProgrammingError(string? errorCode)
    {
        var operation = NewOperation();
        operation.RecordAttemptStarted(Now);

        Assert.ThrowsAny<ArgumentException>(() => operation.Fail(errorCode!, Later));
    }

    [Fact]
    public void FailThenRetryThenComplete_IsAValidLifecycle()
    {
        var operation = NewOperation();

        operation.RecordAttemptStarted(Now);
        operation.Fail("district.unavailable", Now.AddSeconds(10));
        operation.RecordAttemptStarted(Later);
        var result = operation.Complete(Later.AddSeconds(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(ProvisioningStatus.Completed, operation.Status);
        Assert.Equal(2, operation.AttemptCount);
    }
}
