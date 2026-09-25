using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.Application.Districts.ListDistricts;

/// <summary>Reads a page of the district registry for the admin view. Never writes.</summary>
public sealed class ListDistrictsHandler(IDistrictRepository districts) : IQueryHandler<ListDistrictsQuery, ListDistrictsResult>
{
    public async Task<Result<ListDistrictsResult>> HandleAsync(ListDistrictsQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize, skip) = Pagination.Normalize(query.Page, query.PageSize);

        var paged = await districts.ListAsync(skip, pageSize, cancellationToken);

        var items = paged.Items
            .Select(d => new DistrictSummary(
                d.Id, d.Code, d.Name, d.ApiBaseUrl.ToString(), d.Status, d.CreatedAt, d.UpdatedAt, d.ActivatedAt, d.DeactivatedAt))
            .ToList();

        return Result.Success(new ListDistrictsResult(items, paged.TotalCount, page, pageSize));
    }
}
