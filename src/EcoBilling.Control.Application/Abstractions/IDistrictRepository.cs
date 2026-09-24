using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Abstractions;

/// <summary>Access to the district registry.</summary>
public interface IDistrictRepository
{
    /// <summary>
    /// Looks up a district by its normalized code, in any status. The caller -- not
    /// this method -- decides whether an inactive district is reported to the client
    /// as <c>district.not_found</c> or <c>district.inactive</c>; hiding that distinction
    /// here would take away a decision that belongs to the scenario.
    /// </summary>
    /// <remarks>
    /// Used by ResolveDistrict (Stage 3) for its lookup, and reused by CreateDistrict
    /// (Stage 5) as the code-conflict pre-check -- nothing about this query is specific
    /// to either scenario.
    /// </remarks>
    Task<District?> GetByNormalizedCodeAsync(string normalizedCode, CancellationToken cancellationToken);

    /// <summary>
    /// Looks up a district by id, returning it tracked so that mutating it through its
    /// own methods (<c>Rename</c>, <c>Activate</c>, ...) and then calling
    /// <see cref="IUnitOfWork.SaveChangesAsync"/> persists the change with no separate
    /// update call. Used by UpdateDistrict, ActivateDistrict and DeactivateDistrict
    /// (Stage 5), none of which need the no-tracking read <see cref="GetByNormalizedCodeAsync"/>
    /// uses for the high-frequency resolve path.
    /// </summary>
    Task<District?> GetByIdAsync(DistrictId id, CancellationToken cancellationToken);

    /// <summary>
    /// Stages a new district for insertion. Nothing is persisted until
    /// <see cref="IUnitOfWork.SaveChangesAsync"/> is called.
    /// </summary>
    void Add(District district);
}
