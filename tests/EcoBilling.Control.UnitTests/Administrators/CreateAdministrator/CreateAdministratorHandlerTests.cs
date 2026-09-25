using EcoBilling.Control.Application.Administrators.CreateAdministrator;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.UnitTests.TestDoubles;

namespace EcoBilling.Control.UnitTests.Administrators.CreateAdministrator;

public sealed class CreateAdministratorHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(FakeAdministratorRepository Repository, FakeUnitOfWork UnitOfWork, CreateAdministratorHandler Handler);

    private static Fixture NewFixture()
    {
        var repository = new FakeAdministratorRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new CreateAdministratorHandler(repository, new FakePasswordHasher(), unitOfWork, new FixedClock(Now));

        return new Fixture(repository, unitOfWork, handler);
    }

    [Fact]
    public async Task HandleAsync_RejectsAMalformedEmail_WithoutTouchingTheRepository()
    {
        var fixture = NewFixture();

        var result = await fixture.Handler.HandleAsync(
            new CreateAdministratorCommand("not-an-email", "Ada Lovelace", "a-long-enough-password"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("administrator.invalid_email", result.Error.Code);
        Assert.Empty(fixture.Repository.Added);
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_RejectsATooShortPassword_BeforeCheckingForAnEmailConflict()
    {
        var fixture = NewFixture();

        var result = await fixture.Handler.HandleAsync(
            new CreateAdministratorCommand("admin@example.com", "Ada Lovelace", "short"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("administrator.invalid_password", result.Error.Code);
        Assert.Empty(fixture.Repository.Added);
    }

    [Fact]
    public async Task HandleAsync_RejectsAnEmailThatAlreadyExists()
    {
        var fixture = NewFixture();
        fixture.Repository.Seed(Administrator.Create(
            AdministratorId.New(),
            AdministratorEmail.Create("admin@example.com").Value,
            "Existing admin",
            "hash",
            Now).Value);

        var result = await fixture.Handler.HandleAsync(
            new CreateAdministratorCommand("Admin@Example.com", "Ada Lovelace", "a-long-enough-password"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("administrator.email_conflict", result.Error.Code);
        Assert.Empty(fixture.Repository.Added);
    }

    [Fact]
    public async Task HandleAsync_RejectsAnEmptyFullName()
    {
        var fixture = NewFixture();

        var result = await fixture.Handler.HandleAsync(
            new CreateAdministratorCommand("admin@example.com", "   ", "a-long-enough-password"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("administrator.invalid_full_name", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_CreatesAnActiveAdministrator_AndCommitsExactlyOnce()
    {
        var fixture = NewFixture();

        var result = await fixture.Handler.HandleAsync(
            new CreateAdministratorCommand("admin@example.com", "Ada Lovelace", "a-long-enough-password"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("admin@example.com", result.Value.NormalizedEmail);
        Assert.Single(fixture.Repository.Added);
        Assert.True(fixture.Repository.Added[0].IsActive);
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_NeverStoresThePlainPassword()
    {
        var fixture = NewFixture();
        const string plainPassword = "a-genuinely-secret-password";

        await fixture.Handler.HandleAsync(
            new CreateAdministratorCommand("admin@example.com", "Ada Lovelace", plainPassword),
            CancellationToken.None);

        Assert.NotEqual(plainPassword, fixture.Repository.Added[0].PasswordHash);
    }
}
