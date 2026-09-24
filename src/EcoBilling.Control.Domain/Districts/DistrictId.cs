namespace EcoBilling.Control.Domain.Districts;

/// <summary>
/// Strongly-typed identifier for a district, so a district id can never be passed
/// where an administrator or operation id is expected.
/// </summary>
public readonly record struct DistrictId(Guid Value)
{
    public static DistrictId Empty => new(Guid.Empty);

    /// <summary>
    /// Creates a new identifier. Version 7 GUIDs are time-ordered, which keeps the
    /// PostgreSQL primary key index append-friendly instead of scattering writes.
    /// </summary>
    public static DistrictId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
