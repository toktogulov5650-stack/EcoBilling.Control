using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.UnitTests.Districts;

public sealed class TrustedApiUrlTests
{
    [Theory]
    [InlineData("https://district-01.example.com")]
    [InlineData("https://district-01.example.com:8443")]
    [InlineData("https://district-01.example.com/api")]
    [InlineData("https://sub.domain.district-01.example.com/api/v1")]
    public void Create_AcceptsHttpsAddresses(string input)
    {
        var result = TrustedApiUrl.Create(input);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("https://district-01.example.com/", "https://district-01.example.com/")]
    [InlineData("https://district-01.example.com/api/", "https://district-01.example.com/api")]
    public void Create_CanonicalizesTheTrailingSlash(string input, string expected)
    {
        var result = TrustedApiUrl.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value.AbsoluteUri);
    }

    [Theory]
    [InlineData("http://district-01.example.com")]
    [InlineData("ftp://district-01.example.com")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://district-01.example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hello")]
    [InlineData("jar:https://district-01.example.com!/")]
    public void Create_RejectsEverySchemeExceptHttps(string input)
    {
        var result = TrustedApiUrl.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal("district.invalid_url", result.Error.Code);
    }

    [Theory]
    [InlineData("https@evil.example.com")]
    [InlineData("https.evil.example.com")]
    [InlineData("httpsevil")]
    public void Create_IsNotFooledByValuesThatMerelyStartWithHttps(string input)
    {
        // A StartsWith("https") check would accept these. Proper URI parsing does not.
        var result = TrustedApiUrl.Create(input);

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("https://user:password@district-01.example.com")]
    [InlineData("https://user@district-01.example.com")]
    public void Create_RejectsCredentialsInTheAuthority(string input)
    {
        Assert.True(TrustedApiUrl.Create(input).IsFailure);
    }

    [Theory]
    [InlineData("https://district-01.example.com?a=b")]
    [InlineData("https://district-01.example.com/api?a=b")]
    [InlineData("https://district-01.example.com#fragment")]
    public void Create_RejectsQueryAndFragment(string input)
    {
        Assert.True(TrustedApiUrl.Create(input).IsFailure);
    }

    [Theory]
    [InlineData("https://localhost")]
    [InlineData("https://localhost:5001")]
    [InlineData("https://api.localhost")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://127.1.2.3")]
    [InlineData("https://0.0.0.0")]
    public void Create_RejectsLoopbackAndUnspecifiedAddresses(string input)
    {
        Assert.True(TrustedApiUrl.Create(input).IsFailure);
    }

    [Theory]
    [InlineData("https://10.0.0.1")]
    [InlineData("https://10.255.255.255")]
    [InlineData("https://172.16.0.1")]
    [InlineData("https://172.31.255.255")]
    [InlineData("https://192.168.1.1")]
    [InlineData("https://100.64.0.1")]
    [InlineData("https://198.18.0.1")]
    public void Create_RejectsPrivateAndReservedIPv4Ranges(string input)
    {
        Assert.True(TrustedApiUrl.Create(input).IsFailure);
    }

    [Theory]
    [InlineData("https://169.254.169.254")]      // AWS/GCP/Azure instance metadata
    [InlineData("https://169.254.0.1")]
    public void Create_RejectsLinkLocalAndCloudMetadataAddresses(string input)
    {
        Assert.True(TrustedApiUrl.Create(input).IsFailure);
    }

    [Theory]
    [InlineData("https://[::1]")]
    [InlineData("https://[::]")]
    [InlineData("https://[fe80::1]")]
    [InlineData("https://[fd00::1]")]
    [InlineData("https://[fc00::1]")]
    public void Create_RejectsLoopbackAndInternalIPv6Ranges(string input)
    {
        Assert.True(TrustedApiUrl.Create(input).IsFailure);
    }

    [Fact]
    public void Create_RejectsIPv4MappedIPv6Loopback()
    {
        Assert.True(TrustedApiUrl.Create("https://[::ffff:127.0.0.1]").IsFailure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    public void Create_RejectsEmptyAndUnparsableInput(string? input)
    {
        Assert.True(TrustedApiUrl.Create(input).IsFailure);
    }

    [Fact]
    public void Create_RejectsInputLongerThanTheGuard()
    {
        var input = "https://" + new string('a', TrustedApiUrl.MaxInputLength) + ".example.com";

        Assert.True(TrustedApiUrl.Create(input).IsFailure);
    }

    [Fact]
    public void Create_AcceptsAPublicIPv4Address()
    {
        // Restricting which public hosts are allowed is the configured allowlist's job,
        // added in a later stage. The domain rule only blocks internal ranges.
        Assert.True(TrustedApiUrl.Create("https://203.0.113.10").IsSuccess);
    }

    [Fact]
    public void Host_ReturnsThePunycodeForm()
    {
        var result = TrustedApiUrl.Create("https://пример.example.com");

        Assert.True(result.IsSuccess);
        Assert.StartsWith("xn--", result.Value.Host, StringComparison.Ordinal);
    }

    [Fact]
    public void Equality_IsBasedOnTheCanonicalAddress()
    {
        var a = TrustedApiUrl.Create("https://district-01.example.com/api/").Value;
        var b = TrustedApiUrl.Create("https://district-01.example.com/api").Value;
        var c = TrustedApiUrl.Create("https://district-02.example.com/api").Value;

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }
}
