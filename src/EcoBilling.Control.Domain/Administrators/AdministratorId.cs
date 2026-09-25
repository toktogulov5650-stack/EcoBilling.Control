namespace EcoBilling.Control.Domain.Administrators;

/// <summary>
/// Strongly-typed identifier for a system administrator, so an administrator id can
/// never be passed where a district or refresh-token id is expected.
/// </summary>
public readonly record struct AdministratorId(Guid Value)
{
    public static AdministratorId Empty => new(Guid.Empty);

    /// <summary>Version 7 GUIDs are time-ordered, keeping the primary key index append-friendly.</summary>
    public static AdministratorId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
