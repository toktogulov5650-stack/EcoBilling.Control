namespace EcoBilling.Control.Domain.Common;

/// <summary>
/// A stable, machine-readable failure code paired with a human-readable message.
/// The code is part of the public API contract; the message is for diagnostics and
/// must never carry secrets, SQL, connection strings or internal addresses.
/// </summary>
public sealed record Error(string Code, string Message)
{
    /// <summary>The absence of an error. Only a successful result may carry this.</summary>
    public static readonly Error None = new(string.Empty, string.Empty);
}
