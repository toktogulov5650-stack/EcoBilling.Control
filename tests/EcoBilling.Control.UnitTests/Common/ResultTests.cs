using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.UnitTests.Common;

public sealed class ResultTests
{
    private static readonly Error SampleError = new("sample.code", "Sample message.");

    [Fact]
    public void Success_HasNoError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_CarriesTheError()
    {
        var result = Result.Failure(SampleError);

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Equal(SampleError, result.Error);
    }

    [Fact]
    public void Failure_WithoutError_IsRejected()
    {
        Assert.Throws<DomainException>(() => Result.Failure(Error.None));
    }

    [Fact]
    public void SuccessOfValue_ExposesTheValue()
    {
        var result = Result.Success("value");

        Assert.True(result.IsSuccess);
        Assert.Equal("value", result.Value);
    }

    [Fact]
    public void FailureOfValue_DoesNotExposeAValue()
    {
        var result = Result.Failure<string>(SampleError);

        Assert.True(result.IsFailure);
        Assert.Throws<DomainException>(() => result.Value);
    }

    [Fact]
    public void Error_ComparesByCodeAndMessage()
    {
        Assert.Equal(new Error("a", "b"), new Error("a", "b"));
        Assert.NotEqual(new Error("a", "b"), new Error("a", "c"));
    }
}
