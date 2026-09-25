namespace EcoBilling.Control.Api.Cors;

/// <summary>
/// Binds the "Cors" configuration section (Stage 18). Purely a web-hosting concern --
/// unlike <c>Districts:AllowedHosts</c> (a district's own trusted API address) or
/// <c>AllowedHosts</c> (which Host header this instance answers to), this controls which
/// browser-side origins may call this API cross-origin at all.
/// </summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>
    /// Exact origins (scheme + host + port) allowed to call this API from a browser.
    /// Empty by default: a missing or empty list means no cross-origin browser access is
    /// allowed, not that the check is skipped -- deny by default in Production. In
    /// Development, this list is not required: see <c>AddApiCors</c>.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];
}
