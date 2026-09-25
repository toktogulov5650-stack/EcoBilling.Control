using System.Globalization;
using EcoBilling.Control.Domain.Administrators;

namespace EcoBilling.Control.UnitTests.Administrators.Domain;

public sealed class AdministratorEmailTests
{
    [Theory]
    [InlineData("admin@example.com")]
    [InlineData("a@b.co")]
    [InlineData("first.last+tag@sub.example.com")]
    public void Create_AcceptsWellFormedAddresses(string input)
    {
        var result = AdministratorEmail.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(input, result.Value.Normalized);
    }

    [Theory]
    [InlineData("Admin@Example.com", "admin@example.com")]
    [InlineData("  admin@example.com  ", "admin@example.com")]
    [InlineData("ADMIN@EXAMPLE.COM", "admin@example.com")]
    public void Create_NormalizesByTrimmingAndLowerCasing(string input, string expected)
    {
        var result = AdministratorEmail.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Normalized);
    }

    [Fact]
    public void Create_KeepsTheOriginalSpellingForDisplay()
    {
        var result = AdministratorEmail.Create("  Admin@Example.com  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Admin@Example.com", result.Value.Original);
        Assert.Equal("admin@example.com", result.Value.Normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign.example.com")]
    [InlineData("@example.com")]
    [InlineData("admin@")]
    [InlineData("admin@@example.com")]
    [InlineData("admin@nodot")]
    [InlineData("admin@.com")]
    [InlineData("admin@example.com.")]
    public void Create_RejectsMalformedAddresses(string? input)
    {
        var result = AdministratorEmail.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal("administrator.invalid_email", result.Error.Code);
    }

    [Fact]
    public void Create_RejectsInputLongerThanTheGuard()
    {
        var local = new string('a', AdministratorEmail.MaxInputLength);
        var result = AdministratorEmail.Create($"{local}@example.com");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Normalize_UsesTheInvariantCulture()
    {
        // Same fix as DistrictCode (Stage 1): under a Turkish locale, culture-sensitive
        // lower-casing maps "I" differently, which would silently produce a value the
        // unique index could never match.
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            Assert.Equal("admin@example.com", AdministratorEmail.Normalize("ADMIN@EXAMPLE.COM"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Equality_IsBasedOnTheNormalizedValue()
    {
        var lower = AdministratorEmail.Create("admin@example.com").Value;
        var upper = AdministratorEmail.Create("ADMIN@EXAMPLE.COM").Value;
        var other = AdministratorEmail.Create("other@example.com").Value;

        Assert.Equal(lower, upper);
        Assert.Equal(lower.GetHashCode(), upper.GetHashCode());
        Assert.NotEqual(lower, other);
    }
}
