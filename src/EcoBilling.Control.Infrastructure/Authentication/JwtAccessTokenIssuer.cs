using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Administrators;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EcoBilling.Control.Infrastructure.Authentication;

/// <summary>Issues signed JWT access tokens, validated by <c>Microsoft.AspNetCore.Authentication.JwtBearer</c> at the Api boundary.</summary>
public sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    private const int MinimumSigningKeyBytes = 32; // 256 bits -- the minimum HMAC-SHA256 should be keyed with.

    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _signingKey;

    public JwtAccessTokenIssuer(IOptions<JwtOptions> options)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            throw new InvalidOperationException(
                $"Missing required configuration '{JwtOptions.SectionName}:SigningKey'. " +
                "Set it via configuration or an environment variable; never commit a real one to appsettings.json.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey);

        if (keyBytes.Length < MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"'{JwtOptions.SectionName}:SigningKey' must be at least {MinimumSigningKeyBytes} bytes (UTF-8) for HMAC-SHA256.");
        }

        _signingKey = new SymmetricSecurityKey(keyBytes);
    }

    public AccessToken Issue(Administrator administrator, DateTimeOffset now)
    {
        var expiresAt = now.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, administrator.Id.Value.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, administrator.NormalizedEmail),
            new Claim("token_type", "access"), // defensive: distinguishes this from any other JWT type this issuer might ever mint.
        };

        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
