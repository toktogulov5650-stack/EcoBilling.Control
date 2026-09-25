namespace EcoBilling.Control.Application.Abstractions;

/// <summary>
/// Normalizes page/pageSize input shared by every paginated query (ListDistricts,
/// ListAuditEntries), so the defaults and the cap live in one place.
/// </summary>
public static class Pagination
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public static (int Page, int PageSize, int Skip) Normalize(int page, int pageSize)
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedPageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        return (normalizedPage, normalizedPageSize, (normalizedPage - 1) * normalizedPageSize);
    }
}
