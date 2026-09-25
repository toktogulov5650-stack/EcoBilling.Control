using EcoBilling.Control.Domain.Administrators;

namespace EcoBilling.Control.UnitTests.Administrators.Domain;

public sealed class PlainTextPasswordTests
{
    [Fact]
    public void Create_AcceptsAPasswordAtExactlyTheMinimumLength()
    {
        var input = new string('a', PlainTextPassword.MinLength);

        var result = PlainTextPassword.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(input, result.Value.Reveal());
    }

    [Fact]
    public void Create_RejectsAPasswordOneCharacterShortOfTheMinimum()
    {
        var input = new string('a', PlainTextPassword.MinLength - 1);

        var result = PlainTextPassword.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal("administrator.invalid_password", result.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Create_RejectsNullOrEmpty(string? input)
    {
        Assert.True(PlainTextPassword.Create(input).IsFailure);
    }

    [Fact]
    public void Create_HasNoUpperBoundOnLength()
    {
        // NIST SP 800-63B (Q10): no forced complexity, no arbitrary short cap either.
        var input = new string('a', 256);

        Assert.True(PlainTextPassword.Create(input).IsSuccess);
    }

    [Fact]
    public void ToString_NeverRevealsTheValue()
    {
        var password = PlainTextPassword.Create("a-genuinely-secret-password").Value;

        Assert.Equal("[REDACTED]", password.ToString());
    }
}
