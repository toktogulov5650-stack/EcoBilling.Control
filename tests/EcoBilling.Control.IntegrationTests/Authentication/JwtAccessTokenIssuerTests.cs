using System.IdentityModel.Tokens.Jwt;
using System.Text;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Infrastructure.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EcoBilling.Control.IntegrationTests.Authentication;

/// <summary>Verifies JWT issuance and that a validator configured the same way Api's JwtBearer scheme is can actually validate the result. No PostgreSQL needed.</summary>
public sealed class JwtAccessTokenIssuerTests
{
    private const string SigningKey = "test-signing-key-at-least-32-bytes-long-for-hmac-sha256";

    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);

    private static JwtAccessTokenIssuer NewIssuer(JwtOptions? options = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options ?? new JwtOptions { SigningKey = SigningKey }));

    private static Administrator NewAdministrator() =>
        Administrator.Create(
            AdministratorId.New(),
            AdministratorEmail.Create("admin@example.com").Value,
            "Ada Lovelace",
            "irrelevant-hash",
            Now).Value;

    [Fact]
    public void Issue_ProducesATokenThatValidatesAgainstTheSameKeyIssuerAndAudience()
    {
        // Real-time now, not the fixture's fixed `Now`: JwtSecurityTokenHandler.ValidateToken
        // checks expiry against the actual system clock, which it cannot be given a fake
        // for -- a token minted against a stale fixed "now" would look expired here on
        // any day other than the one that constant was written on.
        var now = DateTimeOffset.UtcNow;
        var options = new JwtOptions { SigningKey = SigningKey, Issuer = "test-issuer", Audience = "test-audience" };
        var issuer = NewIssuer(options);
        var administrator = NewAdministrator();

        var token = issuer.Issue(administrator, now);

        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear(); // same fix as MapInboundClaims=false (Api); this raw handler has its own default remapping.
        var principal = handler.ValidateToken(token.Value, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            ValidateLifetime = true,
        }, out _);

        Assert.Equal(administrator.Id.Value.ToString(), principal.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        Assert.Equal(administrator.NormalizedEmail, principal.FindFirst(JwtRegisteredClaimNames.Email)!.Value);
        Assert.Equal("access", principal.FindFirst("token_type")!.Value);
    }

    [Fact]
    public void Issue_SetsExpiryAccordingToTheConfiguredLifetime()
    {
        var issuer = NewIssuer(new JwtOptions { SigningKey = SigningKey, AccessTokenLifetimeMinutes = 15 });

        var token = issuer.Issue(NewAdministrator(), Now);

        Assert.Equal(Now.AddMinutes(15), token.ExpiresAt);
    }

    [Fact]
    public void Issue_ProducesADifferentTokenForDifferentAdministrators()
    {
        var issuer = NewIssuer();

        var first = issuer.Issue(NewAdministrator(), Now);
        var second = issuer.Issue(NewAdministrator(), Now);

        Assert.NotEqual(first.Value, second.Value);
    }

    [Fact]
    public void Constructor_ThrowsWhenTheSigningKeyIsMissing()
    {
        Assert.Throws<InvalidOperationException>(() => NewIssuer(new JwtOptions { SigningKey = "" }));
    }

    [Fact]
    public void Constructor_ThrowsWhenTheSigningKeyIsTooShortForHmacSha256()
    {
        Assert.Throws<InvalidOperationException>(() => NewIssuer(new JwtOptions { SigningKey = "too-short" }));
    }

    [Fact]
    public void ATokenSignedWithADifferentKey_FailsValidation()
    {
        var issuer = NewIssuer(new JwtOptions { SigningKey = SigningKey });
        var token = issuer.Issue(NewAdministrator(), Now);

        var handler = new JwtSecurityTokenHandler();
        var wrongKey = "a-completely-different-key-also-at-least-32-bytes-long";

        Assert.ThrowsAny<Exception>(() => handler.ValidateToken(token.Value, new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(wrongKey)),
            ValidateLifetime = false,
        }, out _));
    }
}
