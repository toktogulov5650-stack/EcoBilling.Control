using EcoBilling.Control.Application.Abstractions;

namespace EcoBilling.Control.UnitTests.TestDoubles;

internal sealed class FakeDistrictHostAllowlist(params string[] allowedHosts) : IDistrictHostAllowlist
{
    private readonly HashSet<string> _allowedHosts = new(allowedHosts, StringComparer.OrdinalIgnoreCase);

    public bool IsAllowed(string host) => _allowedHosts.Contains(host);
}
