using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using EcoBilling.Control.Domain.Districts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EcoBilling.Control.Infrastructure.Authentication;

/// <summary>
/// Mints the short-lived signed JWT Control presents to a district's EcoBilling instance
/// to authenticate the CreateDirector/ResetDirectorPassword calls (Stage 8, section 9.1).
/// Intra-Infrastructure only: <see cref="DistrictClients.DistrictClient"/> is its only
/// consumer, so unlike <c>IAccessTokenIssuer</c> this interface has no reason to live in
/// Application.
/// </summary>
public interface IServiceAssertionIssuer
{
    /// <summary>
    /// Issues a token scoped to exactly one district: <c>aud</c> is that district's
    /// <see cref="District.NormalizedCode"/>, so a token captured in transit or logged by
    /// mistake cannot be replayed against a different district even within its short
    /// lifetime.
    /// </summary>
    string Issue(District district, DateTimeOffset now);
}

/// <summary>
/// ES256 (asymmetric), not the HMAC-SHA256 the administrator-login JWT uses (Stage 6):
/// the verifier here is a different process -- a district's own EcoBilling instance --
/// which must be able to check Control's signature without ever holding a secret that
/// could itself mint a valid assertion. Rotation is manual for now (Stage 8, section
/// 9.1): generate a new keypair, distribute the new public key value to each district's
/// configuration, cut over once confirmed. Revocation needs no denylist -- a ≤2-minute
/// lifetime means a compromised key stops being useful as soon as Control stops signing
/// with it.
/// </summary>
public sealed class ServiceAssertionIssuer : IServiceAssertionIssuer, IDisposable
{
    private readonly ServiceAssertionOptions _options;
    private readonly ECDsa _ecdsa;
    private readonly ECDsaSecurityKey _signingKey;

    public ServiceAssertionIssuer(IOptions<ServiceAssertionOptions> options)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.SigningKeyPem))
        {
            throw new InvalidOperationException(
                $"Missing required configuration '{ServiceAssertionOptions.SectionName}:SigningKeyPem'. " +
                "Set it via configuration or an environment variable; never commit a real one to appsettings.json.");
        }

        _ecdsa = ECDsa.Create();
        _ecdsa.ImportFromPem(_options.SigningKeyPem);
        _signingKey = new ECDsaSecurityKey(_ecdsa);
    }

    public string Issue(District district, DateTimeOffset now)
    {
        var expiresAt = now.AddMinutes(_options.LifetimeMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, _options.Issuer),
            // Unique per call. Short lifetime already bounds replay to a couple of
            // minutes; a district MAY additionally track recently-seen jti values for
            // stricter single-use enforcement within that window, but is not required to.
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            _options.Issuer,
            district.NormalizedCode,
            claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.EcdsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void Dispose() => _ecdsa.Dispose();
}
