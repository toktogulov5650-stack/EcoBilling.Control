namespace EcoBilling.Control.Application.Abstractions;

/// <summary>
/// The deployment's configured list of hosts a district's <c>ApiBaseUrl</c> is allowed
/// to use.
/// </summary>
/// <remarks>
/// Layered on top of, not a replacement for, <c>TrustedApiUrl</c>'s own structural SSRF
/// checks (scheme, no userinfo, no private/loopback/link-local/metadata IP literals --
/// Stage 1). Those checks are universal and apply to every address regardless of
/// deployment; this check is deployment-specific configuration, which is why it lives
/// here rather than inside <c>TrustedApiUrl.Create</c>: that method also runs when
/// rehydrating an already-approved row from the database, and re-checking a stored
/// address against today's allowlist on every read would make shrinking the allowlist
/// silently break districts that were valid when they were created.
/// </remarks>
public interface IDistrictHostAllowlist
{
    /// <param name="host">
    /// The Punycode (IDN-ASCII) form of the host, as returned by
    /// <c>TrustedApiUrl.Host</c>, so that Unicode hostnames compare consistently.
    /// </param>
    bool IsAllowed(string host);
}
