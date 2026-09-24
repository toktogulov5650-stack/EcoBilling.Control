using EcoBilling.Control.Infrastructure.Districts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace EcoBilling.Control.IntegrationTests.Districts;

/// <summary>
/// Verifies the configured host allowlist (Q18). Deliberately not part of
/// <see cref="Persistence.DatabaseCollection"/> -- this needs no PostgreSQL container and
/// stays fast, even though it lives in IntegrationTests alongside the Postgres-backed
/// tests because it exercises a real Infrastructure component (config binding), not a
/// fake.
/// </summary>
public sealed class DistrictHostAllowlistTests
{
    private static IOptions<DistrictHostAllowlistOptions> BindFrom(params string[] allowedHosts)
    {
        var data = allowedHosts
            .Select((host, index) => new KeyValuePair<string, string?>($"Districts:AllowedHosts:{index}", host));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();

        var options = new DistrictHostAllowlistOptions();
        configuration.GetSection(DistrictHostAllowlistOptions.SectionName).Bind(options);

        return Microsoft.Extensions.Options.Options.Create(options);
    }

    [Fact]
    public void IsAllowed_ReturnsTrue_ForAHostReadFromRealConfigurationBinding()
    {
        var allowlist = new DistrictHostAllowlist(BindFrom("district-01.example.com"));

        Assert.True(allowlist.IsAllowed("district-01.example.com"));
    }

    [Fact]
    public void IsAllowed_IsCaseInsensitive()
    {
        var allowlist = new DistrictHostAllowlist(BindFrom("District-01.Example.com"));

        Assert.True(allowlist.IsAllowed("district-01.example.com"));
    }

    [Fact]
    public void IsAllowed_ReturnsFalse_ForAHostNotOnTheList()
    {
        var allowlist = new DistrictHostAllowlist(BindFrom("district-01.example.com"));

        Assert.False(allowlist.IsAllowed("district-02.example.com"));
    }

    [Fact]
    public void IsAllowed_ReturnsFalseForEveryHost_WhenTheListIsEmpty()
    {
        // Fail-closed default: an empty or absent configuration means nothing is
        // allowed, not that the check is skipped.
        var allowlist = new DistrictHostAllowlist(BindFrom());

        Assert.False(allowlist.IsAllowed("district-01.example.com"));
    }

    [Fact]
    public void Options_DefaultToAnEmptyList_WhenTheSectionIsEntirelyMissing()
    {
        var configuration = new ConfigurationBuilder().Build(); // no "Districts" section at all

        var options = new DistrictHostAllowlistOptions();
        configuration.GetSection(DistrictHostAllowlistOptions.SectionName).Bind(options);

        Assert.Empty(options.AllowedHosts);
    }
}
