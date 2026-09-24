namespace EcoBilling.Control.Domain.Districts;

/// <summary>
/// Lifecycle state of a district in the registry.
/// Deliberately an enum rather than a boolean: the registry has to record when a
/// district was activated and deactivated, and an enum leaves room for further
/// states without a schema-breaking change.
/// </summary>
public enum DistrictStatus
{
    /// <summary>Known to the registry but must not receive traffic.</summary>
    Inactive = 0,

    /// <summary>Operating; <c>ResolveDistrict</c> will return its API address.</summary>
    Active = 1,
}
