namespace EcoBilling.Control.Infrastructure.Authentication;

/// <summary>Binds the "Authentication:Jwt" configuration section.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Authentication:Jwt";

    /// <summary>
    /// The symmetric HMAC-SHA256 signing key. No default: <see cref="AddAdministratorAuthentication"/>
    /// throws at startup if this is missing, the same way <c>AddPersistence</c> (Stage 4)
    /// treats the database connection string. Never a real value in appsettings.json.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "EcoBilling.Control";

    public string Audience { get; set; } = "EcoBilling.Control";

    /// <summary>Access token lifetime (Q5/Q6: 15 minutes, confirmed).</summary>
    public int AccessTokenLifetimeMinutes { get; set; } = 15;
}
