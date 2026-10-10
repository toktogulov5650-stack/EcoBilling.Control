using EcoBilling.Control.Application.Administrators.ResetAdministratorPassword;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.UnitTests.TestDoubles;

namespace EcoBilling.Control.UnitTests.Administrators.ResetAdministratorPassword;

public sealed class ResetAdministratorPasswordHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 14, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(
        FakeAdministratorRepository Administrators,
        FakeRefreshTokenRepository RefreshTokens,
        FakePasswordHasher PasswordHasher,
        FakeAuditWriter AuditWriter,
        FakeUnitOfWork UnitOfWork,
        ResetAdministratorPasswordHandler Handler);

    private static Fixture NewFixture()
    {
        var administrators = new FakeAdministratorRepository();
        var refreshTokens = new FakeRefreshTokenRepository();
        var passwordHasher = new FakePasswordHasher();
        var auditWriter = new FakeAuditWriter();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new ResetAdministratorPasswordHandler(
            administrators,
            refreshTokens,
            passwordHasher,
            auditWriter,
            unitOfWork,
            new FixedClock(Now));

        return new Fixture(administrators, refreshTokens, passwordHasher, auditWriter, unitOfWork, handler);
    }

    [Fact]
    public async Task HandleAsync_ResetsPassword_RevokesActiveSessions_AndAuditsOnce()
    {
        var fixture = NewFixture();
        var administrator = Administrator.Create(
            AdministratorId.New(),
            AdministratorEmail.Create("superadmin@gmail.com").Value,
            "SuperAdmin",
            "old-hash",
            Now.AddDays(-1)).Value;
        fixture.Administrators.Seed(administrator);

        var activeToken = RefreshToken.IssueNew(
            RefreshTokenId.New(),
            administrator.Id,
            Now.AddMinutes(-1),
            TimeSpan.FromDays(1)).Token;
        fixture.RefreshTokens.Seed(activeToken);

        var result = await fixture.Handler.HandleAsync(
            new ResetAdministratorPasswordCommand("SuperAdmin@Gmail.com", "a-new-secure-password"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("superadmin@gmail.com", result.Value.NormalizedEmail);
        Assert.True(fixture.PasswordHasher.Verify(administrator.PasswordHash, "a-new-secure-password"));
        Assert.False(activeToken.IsActive(Now));
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount);

        var entry = Assert.Single(fixture.AuditWriter.Entries);
        Assert.Equal("administrator.password_reset", entry.Action);
        Assert.Equal(administrator.Id.Value.ToString(), entry.EntityId);
        Assert.DoesNotContain("a-new-secure-password", entry.AfterData, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_RejectsAShortPasswordWithoutChangingTheHash()
    {
        var fixture = NewFixture();
        var administrator = Administrator.Create(
            AdministratorId.New(),
            AdministratorEmail.Create("superadmin@gmail.com").Value,
            "SuperAdmin",
            "old-hash",
            Now.AddDays(-1)).Value;
        fixture.Administrators.Seed(administrator);

        var result = await fixture.Handler.HandleAsync(
            new ResetAdministratorPasswordCommand("superadmin@gmail.com", "short"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("administrator.invalid_password", result.Error.Code);
        Assert.Equal("old-hash", administrator.PasswordHash);
        Assert.Equal("administrator.password_reset_failed", Assert.Single(fixture.AuditWriter.Entries).Action);
    }

    [Fact]
    public async Task HandleAsync_ReturnsNotFoundForAnUnknownAdministrator()
    {
        var fixture = NewFixture();

        var result = await fixture.Handler.HandleAsync(
            new ResetAdministratorPasswordCommand("missing@example.com", "a-new-secure-password"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("administrator.not_found", result.Error.Code);
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount);
    }
}
