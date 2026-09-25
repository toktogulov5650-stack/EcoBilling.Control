using EcoBilling.Control.Domain.Administrators;

namespace EcoBilling.Control.UnitTests.Administrators.Domain;

public sealed class AdministratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(1);

    private static Administrator NewAdministrator(string fullName = "Ada Lovelace") =>
        Administrator.Create(
            AdministratorId.New(),
            AdministratorEmail.Create("admin@example.com").Value,
            fullName,
            "some-password-hash",
            Now).Value;

    [Fact]
    public void Create_StartsActive()
    {
        // Unlike District, there is no ActivateAdministrator scenario in this stage --
        // an administrator that started inactive could never be enabled.
        var administrator = NewAdministrator();

        Assert.True(administrator.IsActive);
    }

    [Fact]
    public void Create_SetsTheAuditTimestamps()
    {
        var administrator = NewAdministrator();

        Assert.Equal(Now, administrator.CreatedAt);
        Assert.Equal(Now, administrator.UpdatedAt);
        Assert.Null(administrator.LastLoginAt);
    }

    [Fact]
    public void Create_StoresBothTheOriginalAndNormalizedEmail()
    {
        var email = AdministratorEmail.Create("Admin@Example.com").Value;

        var administrator = Administrator.Create(
            AdministratorId.New(), email, "Ada Lovelace", "hash", Now).Value;

        Assert.Equal("Admin@Example.com", administrator.Email);
        Assert.Equal("admin@example.com", administrator.NormalizedEmail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsAnEmptyFullName(string? fullName)
    {
        var result = Administrator.Create(
            AdministratorId.New(),
            AdministratorEmail.Create("admin@example.com").Value,
            fullName,
            "hash",
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("administrator.invalid_full_name", result.Error.Code);
    }

    [Fact]
    public void Create_RejectsAFullNameLongerThanTheLimit()
    {
        var result = Administrator.Create(
            AdministratorId.New(),
            AdministratorEmail.Create("admin@example.com").Value,
            new string('a', Administrator.MaxFullNameLength + 1),
            "hash",
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("administrator.invalid_full_name", result.Error.Code);
    }

    [Fact]
    public void Create_TrimsTheFullName()
    {
        var administrator = NewAdministrator("  Ada Lovelace  ");

        Assert.Equal("Ada Lovelace", administrator.FullName);
    }

    [Fact]
    public void Create_RejectsAnEmptyIdAsAProgrammingError()
    {
        Assert.Throws<ArgumentException>(() => Administrator.Create(
            AdministratorId.Empty,
            AdministratorEmail.Create("admin@example.com").Value,
            "Ada Lovelace",
            "hash",
            Now));
    }

    [Fact]
    public void Create_RejectsAnEmptyPasswordHashAsAProgrammingError()
    {
        Assert.Throws<ArgumentException>(() => Administrator.Create(
            AdministratorId.New(),
            AdministratorEmail.Create("admin@example.com").Value,
            "Ada Lovelace",
            "   ",
            Now));
    }

    [Fact]
    public void RecordLogin_StampsLastLoginAtAndUpdatedAt()
    {
        var administrator = NewAdministrator();

        administrator.RecordLogin(Later);

        Assert.Equal(Later, administrator.LastLoginAt);
        Assert.Equal(Later, administrator.UpdatedAt);
    }

    [Fact]
    public void PasswordHashCannotBeChangedThroughAnyPublicMethod()
    {
        // Never returned through any API response (brief's mandate). This proves there
        // is also no public mutator for it beyond construction -- the only way it
        // changes is by creating a new Administrator.
        var mutators = typeof(Administrator)
            .GetMethods()
            .Where(m => m.Name.Contains("PasswordHash", StringComparison.Ordinal) && m.Name.StartsWith("set_", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(mutators);
    }
}
