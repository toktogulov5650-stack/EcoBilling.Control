using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Infrastructure.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EcoBilling.Control.IntegrationTests.Authentication;

/// <summary>
/// Verifies the ES256 service-assertion issuer (Stage 8, section 9.1) and that a
/// validator holding only the public key -- exactly what a district's EcoBilling
/// instance would be configured with -- can actually verify the result. No PostgreSQL
/// needed.
/// </summary>
public sealed class ServiceAssertionIssuerTests
{
    private const string SigningKeyPem = """
        -----BEGIN EC PRIVATE KEY-----
        MHcCAQEEIM/Zmsn7TVLlASCs4fwrBHO+Z4+RMX0S/798BLR39S9soAoGCCqGSM49
        AwEHoUQDQgAErNpguOsMTV/j7ZfEW7Y4Lbtbl1gwdXQ7kEB9ZQWhTxZDRvANZoN2
        9tsaUpXl4W92V86YoF+I9ffOdEDkDNVhYA==
        -----END EC PRIVATE KEY-----
        """;

    private const string DifferentSigningKeyPem = """
        -----BEGIN EC PRIVATE KEY-----
        MHcCAQEEIO0Wkr0UbCJN3852iE/OXtg+8EsHU8eSBaQFuhGRGXBnoAoGCCqGSM49
        AwEHoUQDQgAERC2AP+zeqttPfasTouGTjFaCIwwB/hZzxUBrQTEb5+hVRv6unIc7
        buMEeXK1vvmdeQBzDF9YcOhFsqFiuH9dKg==
        -----END EC PRIVATE KEY-----
        """;

    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    private static ServiceAssertionIssuer NewIssuer(ServiceAssertionOptions? options = null) =>
        new(Options.Create(options ?? new ServiceAssertionOptions { SigningKeyPem = SigningKeyPem }));

    private static District NewDistrict(string code = "BISHKEK-01") =>
        District.Create(
            DistrictId.New(),
            DistrictCode.Create(code).Value,
            "Bishkek district",
            TrustedApiUrl.Create("https://district-01.example.com/api").Value,
            Now).Value;

    private static ECDsa PublicKeyOnly(string pem)
    {
        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(pem);

        // A district only ever holds Control's PUBLIC key (Stage 8, section 9.1) -- this
        // round-trips through the exported public parameters so the validation test
        // below cannot accidentally rely on also having the private key.
        var publicOnly = ECDsa.Create();
        publicOnly.ImportParameters(new ECParameters { Curve = ecdsa.ExportParameters(false).Curve, Q = ecdsa.ExportParameters(false).Q });
        ecdsa.Dispose();

        return publicOnly;
    }

    [Fact]
    public void Issue_ProducesATokenThatValidatesAgainstOnlyThePublicKey()
    {
        var issuer = NewIssuer(new ServiceAssertionOptions { SigningKeyPem = SigningKeyPem, Issuer = "ecobilling-control" });
        var district = NewDistrict();
        var now = DateTimeOffset.UtcNow; // real time: ValidateToken checks expiry against the system clock.

        var token = issuer.Issue(district, now);

        using var publicKey = PublicKeyOnly(SigningKeyPem);
        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear(); // same fix as MapInboundClaims=false (Api); this raw handler has its own default remapping.
        var principal = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "ecobilling-control",
            ValidateAudience = true,
            ValidAudience = district.NormalizedCode,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new ECDsaSecurityKey(publicKey),
            ValidateLifetime = true,
        }, out _);

        Assert.Equal("ecobilling-control", principal.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        Assert.NotNull(principal.FindFirst(JwtRegisteredClaimNames.Jti));
    }

    [Fact]
    public void Issue_BindsTheAudienceToTheSpecificDistrict()
    {
        var issuer = NewIssuer();
        var thisDistrict = NewDistrict("BISHKEK-01");
        var otherDistrict = NewDistrict("OSH-01");

        var token = issuer.Issue(thisDistrict, Now);

        using var publicKey = PublicKeyOnly(SigningKeyPem);
        var handler = new JwtSecurityTokenHandler();

        // A token minted for BISHKEK-01 must be rejected by a validator expecting OSH-01
        // -- this is exactly the replay protection the audience binding exists for.
        Assert.ThrowsAny<Exception>(() => handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = true,
            ValidAudience = otherDistrict.NormalizedCode,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new ECDsaSecurityKey(publicKey),
            ValidateLifetime = false,
        }, out _));
    }

    [Fact]
    public void Issue_SetsExpiryAccordingToTheConfiguredLifetime()
    {
        var issuer = NewIssuer(new ServiceAssertionOptions { SigningKeyPem = SigningKeyPem, LifetimeMinutes = 2 });
        var token = issuer.Issue(NewDistrict(), Now);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal(Now.AddMinutes(2).UtcDateTime, jwt.ValidTo);
    }

    [Fact]
    public void Issue_ProducesADifferentTokenEveryTime_EvenForTheSameDistrictAndInstant()
    {
        // The jti claim is a fresh random value per call -- two tokens for the exact
        // same district and timestamp must still differ.
        var issuer = NewIssuer();
        var district = NewDistrict();

        var first = issuer.Issue(district, Now);
        var second = issuer.Issue(district, Now);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Constructor_ThrowsWhenTheSigningKeyIsMissing()
    {
        Assert.Throws<InvalidOperationException>(() => NewIssuer(new ServiceAssertionOptions { SigningKeyPem = "" }));
    }

    [Fact]
    public void ATokenSignedWithADifferentKey_FailsValidation()
    {
        var issuer = NewIssuer(new ServiceAssertionOptions { SigningKeyPem = SigningKeyPem });
        var token = issuer.Issue(NewDistrict(), Now);

        using var wrongPublicKey = PublicKeyOnly(DifferentSigningKeyPem);
        var handler = new JwtSecurityTokenHandler();

        Assert.ThrowsAny<Exception>(() => handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new ECDsaSecurityKey(wrongPublicKey),
            ValidateLifetime = false,
        }, out _));
    }
}
