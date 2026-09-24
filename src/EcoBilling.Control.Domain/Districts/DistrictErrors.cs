using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.Domain.Districts;

/// <summary>Stable error codes for district rules.</summary>
public static class DistrictErrors
{
    public static readonly Error InvalidCode = new(
        "district.invalid_code",
        "The district code is not in a valid format.");

    public static readonly Error InvalidUrl = new(
        "district.invalid_url",
        "The district API address is not an accepted trusted address.");

    public static readonly Error NotFound = new(
        "district.not_found",
        "No district matches the supplied code.");

    public static readonly Error Inactive = new(
        "district.inactive",
        "The district is not active.");

    public static readonly Error CodeConflict = new(
        "district.code_conflict",
        "A district with this code already exists.");

    // NOTE: the two codes below are not in the error catalogue of the brief (section 20).
    // They are added because the domain needs them and no existing code fits.
    // Both are flagged for confirmation before the public API contract is frozen.
    public static readonly Error InvalidName = new(
        "district.invalid_name",
        "The district name is empty or too long.");

    public static readonly Error AlreadyActive = new(
        "district.invalid_status_transition",
        "The district is already active.");

    public static readonly Error AlreadyInactive = new(
        "district.invalid_status_transition",
        "The district is already inactive.");
}
