using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Districts.GetDistrict;

public sealed record GetDistrictResult(
    DistrictId DistrictId,
    string Code,
    string Name,
    string ApiBaseUrl,
    DistrictStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? DeactivatedAt);
