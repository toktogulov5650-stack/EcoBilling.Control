using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Administrators;

namespace EcoBilling.Control.UnitTests.TestDoubles;

internal sealed class FakeRefreshTokenRepository : IRefreshTokenRepository
{
    private readonly Dictionary<string, RefreshToken> _byTokenHash = new(StringComparer.Ordinal);

    public List<RefreshToken> Added { get; } = [];

    public void Seed(RefreshToken token) => _byTokenHash[token.TokenHash] = token;

    public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        Task.FromResult(_byTokenHash.GetValueOrDefault(tokenHash));

    public Task<IReadOnlyList<RefreshToken>> GetActiveByAdministratorIdAsync(
        AdministratorId administratorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RefreshToken> active = _byTokenHash.Values
            .Where(t => t.AdministratorId == administratorId && t.IsActive(now))
            .ToList();

        return Task.FromResult(active);
    }

    public void Add(RefreshToken refreshToken)
    {
        Added.Add(refreshToken);
        Seed(refreshToken);
    }
}
