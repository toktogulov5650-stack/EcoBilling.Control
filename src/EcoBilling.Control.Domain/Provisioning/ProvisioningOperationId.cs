namespace EcoBilling.Control.Domain.Provisioning;

/// <summary>
/// Strongly-typed identifier for a provisioning operation, so it can never be passed
/// where a district or administrator id is expected.
/// </summary>
public readonly record struct ProvisioningOperationId(Guid Value)
{
    public static ProvisioningOperationId Empty => new(Guid.Empty);

    /// <summary>
    /// Creates a new identifier. Version 7 GUIDs are time-ordered, which keeps the
    /// PostgreSQL primary key index append-friendly instead of scattering writes.
    /// </summary>
    public static ProvisioningOperationId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
