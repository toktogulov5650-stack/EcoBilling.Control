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
/// </remarks>
public sealed class ResolveDistrictHandler(IDistrictRepository districts)
    : IQueryHandler<ResolveDistrictQuery, ResolveDistrictResult>
{
    /// <summary>
    /// How long the client may trust the returned address before resolving again.
    /// </summary>
    /// <remarks>
    /// A fixed constant of the public contract (architecture doc, section 19.1). This is
    /// deliberately NOT the same number as Control's server-side resolve cache TTL, which
    /// is still open (architecture doc, section 26, "Кеш TTL резолва"). The two serve
    /// different audiences and are not meant to be kept in sync -- see the comparison
    /// table in the architecture doc for why tying them together would be a mistake.
    /// </remarks>
    public const int ExpiresInSeconds = 3600;

    public async Task<Result<ResolveDistrictResult>> HandleAsync(
        ResolveDistrictQuery query,
        CancellationToken cancellationToken)
    {
        var codeResult = DistrictCode.Create(query.DistrictCode);

        if (codeResult.IsFailure)
        {
            return Result.Failure<ResolveDistrictResult>(codeResult.Error);
        }

        var district = await districts.GetByNormalizedCodeAsync(codeResult.Value.Normalized, cancellationToken);

        if (district is null)
        {
            return Result.Failure<ResolveDistrictResult>(DistrictErrors.NotFound);
        }

        if (!district.IsActive)
        {
            return Result.Failure<ResolveDistrictResult>(DistrictErrors.Inactive);
        }

        return Result.Success(new ResolveDistrictResult(
            district.NormalizedCode,
            district.ApiBaseUrl.ToString(),
            ExpiresInSeconds));
    }
}
