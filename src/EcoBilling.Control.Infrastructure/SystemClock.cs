using EcoBilling.Control.Application.Abstractions;

namespace EcoBilling.Control.Infrastructure;

/// <summary>The real system clock. No implementation existed before Stage 5, since no earlier scenario needed one.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
