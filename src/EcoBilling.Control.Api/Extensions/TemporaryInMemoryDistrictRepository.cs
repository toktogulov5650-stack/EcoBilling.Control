using System.Collections.Concurrent;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Api.Extensions;

/// <summary>
/// TEMPORARY stand-in for the real PostgreSQL-backed <see cref="IDistrictRepository"/>.
/// </summary>
/// <remarks>
/// Exists only so ResolveDistrict (Stage 3) can be exercised end to end before
/// persistence (Stage 4) is built. Process-local, non-persistent, and starts empty --
/// nothing here fabricates district data.
///
/// <see cref="Seed"/> is not part of <see cref="IDistrictRepository"/>; it exists purely
/// for tests to populate the store directly, since there is no CreateDistrict endpoint
/// yet (Stage 5) to do it through HTTP.
///
/// Delete this file and its DI registration in <see cref="ServiceCollectionExtensions"/>
/// the moment Stage 4 supplies the real repository.
/// </remarks>
public sealed class TemporaryInMemoryDistrictRepository : IDistrictRepository
{
    private readonly ConcurrentDictionary<string, District> _byNormalizedCode = new(StringComparer.Ordinal);

    public void Seed(District district) => _byNormalizedCode[district.NormalizedCode] = district;

    public Task<District?> GetByNormalizedCodeAsync(string normalizedCode, CancellationToken cancellationToken) =>
        Task.FromResult(_byNormalizedCode.GetValueOrDefault(normalizedCode));
}
