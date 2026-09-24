using EcoBilling.Control.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace EcoBilling.Control.Infrastructure.Districts;

/// <summary>Config-backed <see cref="IDistrictHostAllowlist"/> (Q18).</summary>
public sealed class DistrictHostAllowlist : IDistrictHostAllowlist
{
    private readonly HashSet<string> _allowedHosts;

    public DistrictHostAllowlist(IOptions<DistrictHostAllowlistOptions> options) =>
        _allowedHosts = new HashSet<string>(options.Value.AllowedHosts, StringComparer.OrdinalIgnoreCase);

    public bool IsAllowed(string host) => _allowedHosts.Contains(host);
}
