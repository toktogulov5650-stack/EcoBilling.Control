using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Infrastructure.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EcoBilling.Control.IntegrationTests.Authentication;

/// <summary>
/// Verifies the RS256 service-assertion issuer and that a
/// validator holding only the public key -- exactly what a district's EcoBilling
/// instance would be configured with -- can actually verify the result. No PostgreSQL
/// needed.
/// </summary>
public sealed class ServiceAssertionIssuerTests
{
    private static readonly string SigningKeyPem = CreatePrivateKeyPem();
    private static readonly string DifferentSigningKeyPem = CreatePrivateKeyPem();

    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    private static ServiceAssertionIssuer NewIssuer(ServiceAssertionOptions? options = null) =>
        new(Options.Create(options ?? NewOptions(SigningKeyPem)));

    private static ServiceAssertionOptions NewOptions(string signingKeyPem) => new()
    {
        SigningKeyPem = signingKeyPem,
        KeyId = "test-key-1",
        Scope = "ecobilling.directors.provision"
    };

    private static string CreatePrivateKeyPem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportPkcs8PrivateKeyPem();
    }

    private static District NewDistrict(string code = "BISHKEK-01") =>
        District.Create(
            DistrictId.New(),
            DistrictCode.Create(code).Value,
            "Bishkek district",
            TrustedApiUrl.Create("https://district-01.example.com/api").Value,
            Now).Value;

    private static RSA PublicKeyOnly(string pem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);

        // A district only ever holds Control's PUBLIC key (Stage 8, section 9.1) -- this
        // round-trips through the exported public parameters so the validation test
        // below cannot accidentally rely on also having the private key.
        var publicOnly = RSA.Create();
        publicOnly.ImportParameters(rsa.ExportParameters(false));

        return publicOnly;
    }

    [Fact]
    public void Issue_ProducesATokenThatValidatesAgainstOnlyThePublicKey()
    {
        var options = NewOptions(SigningKeyPem);
        options.Issuer = "ecobilling-control";
        var issuer = NewIssuer(options);
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
            IssuerSigningKey = new RsaSecurityKey(publicKey) { KeyId = "test-key-1" },
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateLifetime = true,
        }, out _);

        Assert.Equal("ecobilling-control", principal.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        Assert.NotNull(principal.FindFirst(JwtRegisteredClaimNames.Jti));
        Assert.NotNull(principal.FindFirst(JwtRegisteredClaimNames.Iat));
        Assert.Equal("ecobilling.directors.provision", principal.FindFirst("scope")!.Value);
        Assert.Equal("test-key-1", handler.ReadJwtToken(token).Header.Kid);
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
            IssuerSigningKey = new RsaSecurityKey(publicKey) { KeyId = "test-key-1" },
            ValidateLifetime = false,
        }, out _));
    }

    [Fact]
    public void Issue_SetsExpiryAccordingToTheConfiguredLifetime()
    {
        var options = NewOptions(SigningKeyPem);
        options.LifetimeMinutes = 2;
        var issuer = NewIssuer(options);
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
        var issuer = NewIssuer(NewOptions(SigningKeyPem));
        var token = issuer.Issue(NewDistrict(), Now);

        using var wrongPublicKey = PublicKeyOnly(DifferentSigningKeyPem);
        var handler = new JwtSecurityTokenHandler();

        Assert.ThrowsAny<Exception>(() => handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new RsaSecurityKey(wrongPublicKey) { KeyId = "test-key-1" },
            ValidateLifetime = false,
        }, out _));
    }
}
