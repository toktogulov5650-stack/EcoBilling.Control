using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Districts.ListDistricts;

public sealed record DistrictSummary(
    DistrictId DistrictId,
    string Code,
    string Name,
    string ApiBaseUrl,
    DistrictStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? DeactivatedAt);

public sealed record ListDistrictsResult(IReadOnlyList<DistrictSummary> Items, int TotalCount, int Page, int PageSize);
