using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Districts.GetDistrict;

/// <summary>Reads one district for the admin view. Never writes -- viewing is not itself an audited action.</summary>
public sealed class GetDistrictHandler(IDistrictRepository districts) : IQueryHandler<GetDistrictQuery, GetDistrictResult>
{
    public async Task<Result<GetDistrictResult>> HandleAsync(GetDistrictQuery query, CancellationToken cancellationToken)
    {
        var district = await districts.GetByIdAsync(query.DistrictId, cancellationToken);

        if (district is null)
        {
            return Result.Failure<GetDistrictResult>(DistrictErrors.NotFound);
        }

        return Result.Success(new GetDistrictResult(
            district.Id,
            district.Code,
            district.Name,
            district.ApiBaseUrl.ToString(),
            district.Status,
            district.CreatedAt,
            district.UpdatedAt,
            district.ActivatedAt,
            district.DeactivatedAt));
    }
}
