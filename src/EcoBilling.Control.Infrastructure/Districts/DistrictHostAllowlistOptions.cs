namespace EcoBilling.Control.Infrastructure.Districts;

/// <summary>
/// Binds the "Districts" configuration section (Q18: an explicit, configured allowlist,
/// not a domain-suffix wildcard -- the deployment model for district hosting isn't
/// settled yet, and an explicit list is the safer default while that's undecided).
/// </summary>
/// <remarks>
/// <see cref="AllowedHosts"/> is named plainly, not e.g. "AllowedExactHosts", so that a
/// later relaxation to a suffix rule (a sibling "AllowedHostSuffixes" property) can be
/// added without renaming or restructuring this one -- the config format is meant to be
/// append-only from day one.
/// </remarks>
public sealed class DistrictHostAllowlistOptions
{
    public const string SectionName = "Districts";

    /// <summary>
    /// Exact hostnames a district's ApiBaseUrl is allowed to use, compared
    /// case-insensitively against the Punycode form of the host. Empty by default: a
    /// missing or empty list means nothing is allowed, not that the check is skipped.
    /// </summary>
    public string[] AllowedHosts { get; set; } = [];
}
