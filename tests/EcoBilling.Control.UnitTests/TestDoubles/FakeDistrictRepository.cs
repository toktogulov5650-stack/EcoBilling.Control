using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.UnitTests.TestDoubles;

/// <summary>
/// In-memory <see cref="IDistrictRepository"/> shared by every handler test that needs
/// one, so the fake's behavior (in particular: it never filters out an inactive district,
/// matching the real repository's contract) is defined and tested in exactly one place.
/// </summary>
internal sealed class FakeDistrictRepository : IDistrictRepository
{
    private readonly Dictionary<string, District> _byNormalizedCode = new(StringComparer.Ordinal);
    private readonly Dictionary<DistrictId, District> _byId = [];

    /// <summary>Districts passed to <see cref="Add"/>, in call order -- lets a test assert what, if anything, was added.</summary>
    public List<District> Added { get; } = [];

    public void Seed(District district)
    {
        _byNormalizedCode[district.NormalizedCode] = district;
        _byId[district.Id] = district;
    }

    public Task<District?> GetByNormalizedCodeAsync(string normalizedCode, CancellationToken cancellationToken) =>
        Task.FromResult(_byNormalizedCode.GetValueOrDefault(normalizedCode));

    public Task<District?> GetByIdAsync(DistrictId id, CancellationToken cancellationToken) =>
        Task.FromResult(_byId.GetValueOrDefault(id));

    public void Add(District district)
    {
        Added.Add(district);
        Seed(district);
    }

    public Task<PagedResult<District>> ListAsync(int skip, int take, CancellationToken cancellationToken)
    {
        var ordered = _byId.Values.OrderBy(d => d.NormalizedCode, StringComparer.Ordinal).ToList();

        return Task.FromResult(new PagedResult<District>(ordered.Skip(skip).Take(take).ToList(), ordered.Count));
    }
}
