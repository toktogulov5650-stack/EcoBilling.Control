using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.UnitTests.Districts;

public sealed class DistrictTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(1);

    private static District NewDistrict(string code = "BISHKEK-01", string name = "Bishkek district") =>
        District.Create(
            DistrictId.New(),
            DistrictCode.Create(code).Value,
            name,
            TrustedApiUrl.Create("https://district-01.example.com").Value,
            Now).Value;

    [Fact]
    public void Create_StartsInactiveSoItCannotRouteTrafficBeforeActivation()
    {
        var district = NewDistrict();

        Assert.Equal(DistrictStatus.Inactive, district.Status);
        Assert.False(district.IsActive);
        Assert.Null(district.ActivatedAt);
        Assert.Null(district.DeactivatedAt);
    }

    [Fact]
    public void Create_SetsTheAuditTimestamps()
    {
        var district = NewDistrict();

        Assert.Equal(Now, district.CreatedAt);
        Assert.Equal(Now, district.UpdatedAt);
    }

    [Fact]
    public void Create_StoresBothTheOriginalAndNormalizedCode()
    {
        var district = District.Create(
            DistrictId.New(),
            DistrictCode.Create("bishkek-01").Value,
            "Bishkek district",
            TrustedApiUrl.Create("https://district-01.example.com").Value,
            Now).Value;

        Assert.Equal("bishkek-01", district.Code);
        Assert.Equal("BISHKEK-01", district.NormalizedCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsAnEmptyName(string? name)
    {
        var result = District.Create(
            DistrictId.New(),
            DistrictCode.Create("BISHKEK-01").Value,
            name,
            TrustedApiUrl.Create("https://district-01.example.com").Value,
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("district.invalid_name", result.Error.Code);
    }

    [Fact]
    public void Create_RejectsANameLongerThanTheLimit()
    {
        var result = District.Create(
            DistrictId.New(),
            DistrictCode.Create("BISHKEK-01").Value,
            new string('a', District.MaxNameLength + 1),
            TrustedApiUrl.Create("https://district-01.example.com").Value,
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("district.invalid_name", result.Error.Code);
    }

    [Fact]
    public void Create_TrimsTheName()
    {
        var district = NewDistrict(name: "  Bishkek district  ");

        Assert.Equal("Bishkek district", district.Name);
    }

    [Fact]
    public void Create_RejectsAnEmptyIdAsAProgrammingError()
    {
        Assert.Throws<ArgumentException>(() => District.Create(
            DistrictId.Empty,
            DistrictCode.Create("BISHKEK-01").Value,
            "Bishkek district",
            TrustedApiUrl.Create("https://district-01.example.com").Value,
            Now));
    }

    [Fact]
    public void Activate_MakesTheDistrictActiveAndStampsTheTime()
    {
        var district = NewDistrict();

        var result = district.Activate(Later);

        Assert.True(result.IsSuccess);
        Assert.Equal(DistrictStatus.Active, district.Status);
        Assert.True(district.IsActive);
        Assert.Equal(Later, district.ActivatedAt);
        Assert.Equal(Later, district.UpdatedAt);
    }

    [Fact]
    public void Activate_OnAnActiveDistrictIsRejected()
    {
        var district = NewDistrict();
        district.Activate(Later);

        var result = district.Activate(Later.AddHours(1));

        Assert.True(result.IsFailure);
        Assert.Equal("district.invalid_status_transition", result.Error.Code);
        Assert.Equal(Later, district.UpdatedAt);
    }

    [Fact]
    public void Deactivate_MakesTheDistrictInactiveAndStampsTheTime()
    {
        var district = NewDistrict();
        district.Activate(Later);

        var deactivatedAt = Later.AddHours(1);
        var result = district.Deactivate(deactivatedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(DistrictStatus.Inactive, district.Status);
        Assert.Equal(deactivatedAt, district.DeactivatedAt);
        Assert.Equal(deactivatedAt, district.UpdatedAt);
        Assert.Equal(Later, district.ActivatedAt);
    }

    [Fact]
    public void Deactivate_OnAnInactiveDistrictIsRejected()
    {
        var district = NewDistrict();

        var result = district.Deactivate(Later);

        Assert.True(result.IsFailure);
        Assert.Equal("district.invalid_status_transition", result.Error.Code);
        Assert.Null(district.DeactivatedAt);
        Assert.Equal(Now, district.UpdatedAt);
    }

    [Fact]
    public void ActivateAndDeactivate_CanAlternate()
    {
        var district = NewDistrict();

        Assert.True(district.Activate(Now.AddHours(1)).IsSuccess);
        Assert.True(district.Deactivate(Now.AddHours(2)).IsSuccess);
        Assert.True(district.Activate(Now.AddHours(3)).IsSuccess);

        Assert.Equal(DistrictStatus.Active, district.Status);
        Assert.Equal(Now.AddHours(3), district.ActivatedAt);
        Assert.Equal(Now.AddHours(2), district.DeactivatedAt);
    }

    [Fact]
    public void Rename_ChangesTheNameAndBumpsUpdatedAt()
    {
        var district = NewDistrict();

        var result = district.Rename("  New name  ", Later);

        Assert.True(result.IsSuccess);
        Assert.Equal("New name", district.Name);
        Assert.Equal(Later, district.UpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_RejectsAnEmptyNameAndLeavesTheDistrictUnchanged(string? name)
    {
        var district = NewDistrict();

        var result = district.Rename(name, Later);

        Assert.True(result.IsFailure);
        Assert.Equal("district.invalid_name", result.Error.Code);
        Assert.Equal("Bishkek district", district.Name);
        Assert.Equal(Now, district.UpdatedAt);
    }

    [Fact]
    public void ChangeApiBaseUrl_ReplacesTheAddressAndBumpsUpdatedAt()
    {
        var district = NewDistrict();
        var newUrl = TrustedApiUrl.Create("https://district-99.example.com").Value;

        var result = district.ChangeApiBaseUrl(newUrl, Later);

        Assert.True(result.IsSuccess);
        Assert.Equal(newUrl, district.ApiBaseUrl);
        Assert.Equal(Later, district.UpdatedAt);
    }

    [Fact]
    public void ChangeApiBaseUrl_RejectsNull()
    {
        var district = NewDistrict();

        Assert.Throws<ArgumentNullException>(() => district.ChangeApiBaseUrl(null!, Later));
    }

    [Fact]
    public void TheCodeCannotBeChanged()
    {
        // The district code is immutable until a decision is recorded on whether an
        // operating district may be renumbered. There is deliberately no mutator.
        var mutators = typeof(District)
            .GetMethods()
            .Where(m => m.Name.Contains("Code", StringComparison.Ordinal) && m.Name.StartsWith("set_", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(mutators);
    }
}
