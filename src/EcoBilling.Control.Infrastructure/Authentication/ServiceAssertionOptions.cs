namespace EcoBilling.Control.Infrastructure.Authentication;

/// <summary>Binds the "ServiceAuth:Jwt" configuration section (Stage 8, section 9.1).</summary>
public sealed class ServiceAssertionOptions
{
    public const string SectionName = "ServiceAuth:Jwt";

    /// <summary>
    /// The ES256 private signing key, PEM-encoded. No default: <see cref="ServiceAssertionIssuer"/>
    /// throws at startup if this is missing, the same way <c>JwtOptions.SigningKey</c>
    /// (Stage 6) is treated. Never a real value in appsettings.json. Asymmetric, not
    /// symmetric like the administrator-login JWT (Stage 6): the verifier here is a
    /// different process entirely -- a district's own EcoBilling instance, which must be
    /// able to verify Control's signature without ever holding a secret that could
    /// authenticate AS Control.
    /// </summary>
    public string SigningKeyPem { get; set; } = string.Empty;

    public string Issuer { get; set; } = "ecobilling-control";

    /// <summary>Service-assertion lifetime (Stage 8, section 9.1: 2 minutes, confirmed).</summary>
    public int LifetimeMinutes { get; set; } = 2;
}
