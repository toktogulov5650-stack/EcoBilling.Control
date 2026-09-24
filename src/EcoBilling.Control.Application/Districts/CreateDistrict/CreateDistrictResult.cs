using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Districts.CreateDistrict;

/// <summary>
/// The outcome of creating a district. Minimal on purpose: no HTTP contract has been
/// drafted for the admin endpoints yet (deferred to Stage 6), so this is not shaped as a
/// wire DTO -- just enough for the caller to identify what was created.
/// </summary>
public sealed record CreateDistrictResult(DistrictId DistrictId, string NormalizedCode);
