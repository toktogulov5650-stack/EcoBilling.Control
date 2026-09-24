namespace EcoBilling.Control.Application.Districts.ResolveDistrict;

/// <summary>
/// The public, trusted result of resolving a district code -- exactly what a client is
/// allowed to see.
/// </summary>
/// <remarks>
/// Deliberately excludes DistrictId and DistrictName: a client only needs an address to
/// route to, and returning internal registry identifiers would leak detail no client
/// requires (architecture doc, section 19.1). <see cref="DistrictCode"/> is the
/// normalized, canonical spelling, not necessarily what the client typed.
/// </remarks>
public sealed record ResolveDistrictResult(string DistrictCode, string ApiBaseUrl, int ExpiresInSeconds);
