using EcoBilling.Control.Application.Abstractions;

namespace EcoBilling.Control.UnitTests.TestDoubles;

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}
