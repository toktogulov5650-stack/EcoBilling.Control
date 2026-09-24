namespace EcoBilling.Control.Domain.Common;

/// <summary>
/// Signals that the domain API itself was used incorrectly, for example reading the
/// value of a failed result. This is a programming error, not a business rule failure:
/// business rule failures are returned as <see cref="Result"/>, never thrown.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
