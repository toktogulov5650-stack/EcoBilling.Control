using System.Net;
using System.Net.Sockets;
using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.Domain.Districts;

/// <summary>
/// The API address of a district, validated for use as an outbound request target.
/// </summary>
/// <remarks>
/// Control makes server-side calls to this address, so it is an SSRF sink. Validation
/// therefore parses the URI properly and applies explicit trust rules rather than a
/// prefix check such as <c>StartsWith("https")</c>, which a value like
/// <c>https@evil.test</c> or <c>https:/\/\evil.test</c> can defeat.
///
/// This type enforces the rules that hold everywhere and need no configuration.
/// The deployment-specific allowlist of district hosts is a separate, configured
/// check applied above the domain; it is not decided yet.
/// </remarks>
public sealed class TrustedApiUrl : IEquatable<TrustedApiUrl>
{
    public const int MaxInputLength = 2048;

    private TrustedApiUrl(Uri value) => Value = value;

    public Uri Value { get; }

    /// <summary>The Punycode form of the host, which is what any allowlist must compare.</summary>
    public string Host => Value.IdnHost;

    public static Result<TrustedApiUrl> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > MaxInputLength)
        {
            return Failure();
        }

        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri))
        {
            return Failure();
        }

        // HTTPS only. Blocks http, file, ftp, gopher, jar and every other scheme that
        // turns a URL fetch into something else.
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return Failure();
        }

        // Credentials in the authority (https://user:pass@host) are a redirect and
        // spoofing vector and have no place in a stored service address.
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return Failure();
        }

        // A base address is a prefix that request paths are appended to. A query or
        // fragment would either be silently dropped or corrupt every composed URL.
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return Failure();
        }

        if (uri.HostNameType == UriHostNameType.Unknown || IsDisallowedHost(uri))
        {
            return Failure();
        }

        return Result.Success(new TrustedApiUrl(Canonicalize(uri)));
    }

    private static Result<TrustedApiUrl> Failure() => Result.Failure<TrustedApiUrl>(DistrictErrors.InvalidUrl);

    /// <summary>
    /// Reduces the address to scheme, authority and path with no trailing slash, so that
    /// two spellings of the same address compare equal and produce one cache key.
    /// </summary>
    private static Uri Canonicalize(Uri uri)
    {
        var text = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');

        return new Uri(text, UriKind.Absolute);
    }

    private static bool IsDisallowedHost(Uri uri)
    {
        if (uri.IsLoopback)
        {
            return true;
        }

        var host = uri.IdnHost.Trim('[', ']');

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // A DNS name cannot be judged here: it may resolve anywhere, and resolving it
        // during validation would both be a DNS-rebinding race and make a pure domain
        // rule depend on the network. Name-based restriction is the configured
        // allowlist's job. Literal IP addresses, however, can be judged now.
        if (!IPAddress.TryParse(host, out var ip))
        {
            return false;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        return ip.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsDisallowedIPv4(ip),
            AddressFamily.InterNetworkV6 => IsDisallowedIPv6(ip),
            _ => true,
        };
    }

    private static bool IsDisallowedIPv4(IPAddress ip)
    {
        var b = ip.GetAddressBytes();

        return b[0] switch
        {
            0 => true,                              // 0.0.0.0/8, "this network"
            10 => true,                             // 10.0.0.0/8 private
            127 => true,                            // 127.0.0.0/8 loopback
            100 => b[1] >= 64 && b[1] <= 127,       // 100.64.0.0/10 carrier-grade NAT
            169 => b[1] == 254,                     // 169.254.0.0/16 link-local, incl. cloud metadata
            172 => b[1] >= 16 && b[1] <= 31,        // 172.16.0.0/12 private
            192 => b[1] == 168 || (b[1] == 0 && b[2] == 0), // 192.168.0.0/16 and 192.0.0.0/24
            198 => b[1] is 18 or 19,                // 198.18.0.0/15 benchmarking
            >= 224 => true,                         // multicast, reserved and broadcast
            _ => false,
        };
    }

    private static bool IsDisallowedIPv6(IPAddress ip)
    {
        if (ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.IPv6Loopback))
        {
            return true;
        }

        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || ip.IsIPv6UniqueLocal)
        {
            return true;
        }

        return false;
    }

    public bool Equals(TrustedApiUrl? other) => other is not null && Value.Equals(other.Value);

    public override bool Equals(object? obj) => Equals(obj as TrustedApiUrl);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.AbsoluteUri;
}
