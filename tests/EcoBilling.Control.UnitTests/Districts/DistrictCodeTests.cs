using System.Globalization;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.UnitTests.Districts;

public sealed class DistrictCodeTests
{
    [Theory]
    [InlineData("BISHKEK-01")]
    [InlineData("AB-12")]
    [InlineData("ABCDEFGHIJ-1234")]
    [InlineData("OSH-001")]
    public void Create_AcceptsCodesInTheAgreedFormat(string input)
    {
        var result = DistrictCode.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(input, result.Value.Normalized);
    }

    [Theory]
    [InlineData("bishkek-01", "BISHKEK-01")]
    [InlineData("BiShKeK-01", "BISHKEK-01")]
    [InlineData("  bishkek-01  ", "BISHKEK-01")]
    [InlineData("\tAB-12\n", "AB-12")]
    public void Create_NormalizesByTrimmingAndUpperCasing(string input, string expected)
    {
        var result = DistrictCode.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Normalized);
    }

    [Fact]
    public void Create_KeepsTheOriginalSpellingForDisplay()
    {
        var result = DistrictCode.Create("  bishkek-01  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("bishkek-01", result.Value.Original);
        Assert.Equal("BISHKEK-01", result.Value.Normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A-01")]                // one letter, minimum is two
    [InlineData("ABCDEFGHIJK-01")]      // eleven letters, maximum is ten
    [InlineData("AB-1")]                // one digit, minimum is two
    [InlineData("AB-12345")]            // five digits, maximum is four
    [InlineData("AB_01")]               // wrong separator
    [InlineData("AB 01")]               // space instead of a hyphen
    [InlineData("AB--01")]              // two separators
    [InlineData("ABC")]                 // no separator or digits
    [InlineData("12-34")]               // digits where letters belong
    [InlineData("AB-")]
    [InlineData("-01")]
    [InlineData("AB-01-CD")]            // trailing segment
    [InlineData("ÄB-01")]               // non-ASCII letter
    public void Create_RejectsCodesOutsideTheAgreedFormat(string? input)
    {
        var result = DistrictCode.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal("district.invalid_code", result.Error.Code);
    }

    [Fact]
    public void Create_RejectsNonAsciiDigits()
    {
        // Arabic-Indic digits are matched by the regex class \d but must not be accepted,
        // which is why the pattern uses an explicit [0-9] range.
        var result = DistrictCode.Create("AB-٠١");

        Assert.True(result.IsFailure);
        Assert.Equal("district.invalid_code", result.Error.Code);
    }

    [Fact]
    public void Create_RejectsInputLongerThanTheGuard()
    {
        var result = DistrictCode.Create(new string('A', DistrictCode.MaxInputLength + 1));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Normalize_UsesTheInvariantCulture()
    {
        // Under a Turkish locale a culture-sensitive upper-casing turns "i" into "İ"
        // (U+0130), producing a value the unique index could never match.
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            Assert.Equal("BISHKEK-01", DistrictCode.Normalize("bishkek-01"));
            Assert.Equal("I", DistrictCode.Normalize("i"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Create_UnderTurkishCulture_StillAcceptsCodesContainingI()
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            var result = DistrictCode.Create("bishkek-01");

            Assert.True(result.IsSuccess);
            Assert.Equal("BISHKEK-01", result.Value.Normalized);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Equality_IsBasedOnTheNormalizedValue()
    {
        var lower = DistrictCode.Create("bishkek-01").Value;
        var upper = DistrictCode.Create("BISHKEK-01").Value;
        var other = DistrictCode.Create("OSH-02").Value;

        Assert.Equal(lower, upper);
        Assert.Equal(lower.GetHashCode(), upper.GetHashCode());
        Assert.NotEqual(lower, other);
    }

    [Fact]
    public void ToString_ReturnsTheNormalizedValue()
    {
        Assert.Equal("BISHKEK-01", DistrictCode.Create("bishkek-01").Value.ToString());
    }
}
