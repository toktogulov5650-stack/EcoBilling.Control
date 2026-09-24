namespace EcoBilling.Control.Domain.Common;

/// <summary>
/// The outcome of a domain operation. Expected business failures are represented as
/// values rather than exceptions, so callers must handle them explicitly.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (isSuccess && error != Error.None)
        {
            throw new DomainException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new DomainException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => Result<TValue>.FromValue(value);

    public static Result<TValue> Failure<TValue>(Error error) => Result<TValue>.FromError(error);
}

/// <summary>The outcome of a domain operation that produces a value on success.</summary>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    private Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) => _value = value;

    /// <summary>The produced value. Throws when the result is a failure.</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new DomainException("The value of a failed result cannot be accessed.");

    internal static Result<TValue> FromValue(TValue value) => new(value, true, Error.None);

    internal static Result<TValue> FromError(Error error) => new(default, false, error);
}
