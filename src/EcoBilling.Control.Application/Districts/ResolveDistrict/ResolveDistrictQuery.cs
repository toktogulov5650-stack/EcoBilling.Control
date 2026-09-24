using EcoBilling.Control.Application.Abstractions;

namespace EcoBilling.Control.Application.Districts.ResolveDistrict;

/// <summary>
/// Resolves a district code to its trusted API address.
/// </summary>
/// <remarks>
/// <paramref name="DistrictCode"/> is the raw string as the client sent it. Normalization
/// and format validation happen inside the handler, not here, so the query itself carries
/// no assumption about what a valid code looks like.
/// </remarks>
public sealed record ResolveDistrictQuery(string? DistrictCode) : IQuery<ResolveDistrictResult>;
