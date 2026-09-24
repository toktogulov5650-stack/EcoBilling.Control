using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Abstractions;

/// <summary>
/// Access to the district registry.
/// </summary>
/// <remarks>
/// Scoped to exactly what ResolveDistrict (Stage 3) needs. CreateDistrict and
/// UpdateDistrict (Stage 5) will add the methods they need to this interface then,
/// rather than have this stage guess their shape ahead of time.
/// </remarks>
public interface IDistrictRepository
{
    /// <summary>
    /// Looks up a district by its normalized code, in any status. The caller -- not
    /// this method -- decides whether an inactive district is reported to the client
    /// as <c>district.not_found</c> or <c>district.inactive</c>; hiding that distinction
    /// here would take away a decision that belongs to the scenario.
    /// </summary>
    Task<District?> GetByNormalizedCodeAsync(string normalizedCode, CancellationToken cancellationToken);
}
