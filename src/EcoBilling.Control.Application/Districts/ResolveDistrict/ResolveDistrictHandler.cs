using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Districts.ResolveDistrict;

/// <summary>
/// Looks up the trusted API address for a district code.
/// </summary>
/// <remarks>
/// Never reveals to the caller whether an unmatched code names a district that does not
/// exist or one that is merely inactive: both fail with a different <see cref="Error"/>
/// but the Api layer maps both to the same HTTP status (architecture doc, section 19.1;
/// technical brief, section 9).
///
/// Caches only successful (active, found) resolutions (Stage 12, section 21.1) -- a
/// not_found/inactive result is never cached. This is a deliberate choice, not an
/// oversight: caching a negative result risks a newly created or reactivated district
/// staying invisible for up to the cache TTL after CreateDistrict/ActivateDistrict,
/// with no invalidation call from either to prevent it (they have no prior cache entry
/// to remove). A cache miss on an unknown/inactive code is already a single indexed
/// query -- there is no load problem to solve by caching that path too.
/// </remarks>
public sealed class ResolveDistrictHandler(IDistrictRepository districts, ICache cache)
    : IQueryHandler<ResolveDistrictQuery, ResolveDistrictResult>
{
    /// <summary>
    /// How long the client may trust the returned address before resolving again.
    /// </summary>
    /// <remarks>
    /// A fixed constant of the public contract (architecture doc, section 19.1). This is
    /// deliberately NOT the same number as <see cref="CacheTtl"/>, Control's server-side
    /// resolve cache TTL (Stage 12). The two serve different audiences and are not meant
    /// to be kept in sync -- see the comparison table in the architecture doc for why
    /// tying them together would be a mistake.
    /// </remarks>
    public const int ExpiresInSeconds = 3600;

    /// <summary>Server-side cache TTL (architecture doc, section 26, "Кеш TTL резолва" -- Q12, Stage 12).</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(300);

    public async Task<Result<ResolveDistrictResult>> HandleAsync(
        ResolveDistrictQuery query,
        CancellationToken cancellationToken)
    {
        var codeResult = DistrictCode.Create(query.DistrictCode);

        if (codeResult.IsFailure)
        {
            return Result.Failure<ResolveDistrictResult>(codeResult.Error);
        }

        var normalizedCode = codeResult.Value.Normalized;
        var cacheKey = DistrictCacheKeys.Resolve(normalizedCode);

        var cached = await cache.GetAsync<ResolveDistrictResult>(cacheKey, cancellationToken);

        if (cached is not null)
        {
            return Result.Success(cached);
        }

        var district = await districts.GetByNormalizedCodeAsync(normalizedCode, cancellationToken);

        if (district is null)
        {
            return Result.Failure<ResolveDistrictResult>(DistrictErrors.NotFound);
        }

        if (!district.IsActive)
        {
            return Result.Failure<ResolveDistrictResult>(DistrictErrors.Inactive);
        }

        var result = new ResolveDistrictResult(
            district.NormalizedCode,
            district.ApiBaseUrl.ToString(),
            ExpiresInSeconds);

        await cache.SetAsync(cacheKey, result, CacheTtl, cancellationToken);

        return Result.Success(result);
    }
}
