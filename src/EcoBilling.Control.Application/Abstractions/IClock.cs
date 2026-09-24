namespace EcoBilling.Control.Application.Abstractions;

/// <summary>
/// Abstraction over wall-clock time, so handlers are deterministic under test and
/// Domain never depends on the system clock directly.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
