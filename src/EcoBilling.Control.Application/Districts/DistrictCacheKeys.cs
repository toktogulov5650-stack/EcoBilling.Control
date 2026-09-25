namespace EcoBilling.Control.Application.Districts;

/// <summary>
/// The one cache key format ResolveDistrict's reads and every district-mutating
/// handler's invalidation must agree on -- centralized here so the two sides can never
/// drift apart.
/// </summary>
internal static class DistrictCacheKeys
{
    public static string Resolve(string normalizedCode) => $"district:resolve:{normalizedCode}";
}
