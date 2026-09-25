using EcoBilling.Control.Application.Administrators.Login;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.UnitTests.TestDoubles;

namespace EcoBilling.Control.UnitTests.Administrators.Login;

public sealed class AdministratorLoginHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(
        FakeAdministratorRepository Administrators,
        FakeRefreshTokenRepository RefreshTokens,
        FakePasswordHasher PasswordHasher,
        FakeAuditWriter AuditWriter,
        FakeUnitOfWork UnitOfWork,
        AdministratorLoginHandler Handler);

    private static Fixture NewFixture()
    {
        var administrators = new FakeAdministratorRepository();
        var refreshTokens = new FakeRefreshTokenRepository();
        var passwordHasher = new FakePasswordHasher();
        var auditWriter = new FakeAuditWriter();
        var unitOfWork = new FakeUnitOfWork();
        var accessTokenIssuer = new FakeAccessTokenIssuer(TimeSpan.FromMinutes(15));
        var handler = new AdministratorLoginHandler(
            administrators, refreshTokens, passwordHasher, accessTokenIssuer, auditWriter, unitOfWork, new FixedClock(Now));

        return new Fixture(administrators, refreshTokens, passwordHasher, auditWriter, unitOfWork, handler);
    }

    private static Administrator SeedAdministrator(
        FakeAdministratorRepository repository,
        FakePasswordHasher hasher,
        string email = "admin@example.com",
        string password = "correct-password-123",
        bool isActive = true)
    {
        var administrator = Administrator.Create(
            AdministratorId.New(),
            AdministratorEmail.Create(email).Value,
            "Ada Lovelace",
            hasher.Hash(password),
            Now).Value;

        if (!isActive)
        {
            // Administrator starts active (Stage 6) and this stage has no Deactivate
            // scenario, so reaching an inactive state for a test uses reflection --
            // acceptable here since the point under test is the login handler's
            // behavior given an inactive account, not how one comes to exist.
            typeof(Administrator).GetProperty(nameof(Administrator.IsActive))!
                .SetValue(administrator, false);
        }

        repository.Seed(administrator);

        return administrator;
    }

    [Fact]
    public async Task HandleAsync_RejectsAnUnknownEmail_AndAuditsItWithNoAdministratorId()
    {
        var fixture = NewFixture();

        var result = await fixture.Handler.HandleAsync(
            new AdministratorLoginCommand("unknown@example.com", "anything"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("administrator.invalid_credentials", result.Error.Code);

        // Stage 7 decision: audited anyway -- AdministratorId is null (no account
        // exists), and the attempted email (not a secret) is recorded so a pattern of
        // attempts against non-existent accounts is visible.
        var entry = Assert.Single(fixture.AuditWriter.Entries);
        Assert.Equal("administrator.login_failed", entry.Action);
        Assert.Null(entry.AdministratorId);
        Assert.Contains("unknown@example.com", entry.AfterData!);
    }

    [Fact]
    public async Task HandleAsync_RejectsAWrongPassword_AndAuditsItWithTheKnownAdministratorId()
    {
        var fixture = NewFixture();
        var administrator = SeedAdministrator(fixture.Administrators, fixture.PasswordHasher);

        var result = await fixture.Handler.HandleAsync(
            new AdministratorLoginCommand("admin@example.com", "wrong-password"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("administrator.invalid_credentials", result.Error.Code);

        var entry = Assert.Single(fixture.AuditWriter.Entries);
        Assert.Equal("administrator.login_failed", entry.Action);
        Assert.Equal(administrator.Id.Value, entry.AdministratorId);
    }

    [Fact]
    public async Task HandleAsync_RejectsAnInactiveAdministrator_WithADistinctInternalErrorCode_AndAuditsIt()
    {
        // The Api layer collapses this onto administrator.invalid_credentials (Stage 6
        // design decision); the handler itself still reports the real reason, and the
        // audit entry records that specific reason too (Stage 7).
        var fixture = NewFixture();
        SeedAdministrator(fixture.Administrators, fixture.PasswordHasher, isActive: false);

        var result = await fixture.Handler.HandleAsync(
            new AdministratorLoginCommand("admin@example.com", "correct-password-123"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("administrator.inactive", result.Error.Code);

        var entry = Assert.Single(fixture.AuditWriter.Entries);
        Assert.Equal("administrator.login_failed", entry.Action);
        Assert.Contains("administrator.inactive", entry.AfterData!);
    }

    [Fact]
    public async Task HandleAsync_UnknownEmailAndInactiveAccount_AreIndistinguishableByCodeAlone()
    {
        // administrator.inactive is the ONE internal exception to "collapse everything
        // to invalid_credentials" -- but callers must not be able to tell an unknown
        // email apart from a wrong password by error code either.
        var unknownEmailFixture = NewFixture();
        var wrongPasswordFixture = NewFixture();
        SeedAdministrator(wrongPasswordFixture.Administrators, wrongPasswordFixture.PasswordHasher);

        var unknownResult = await unknownEmailFixture.Handler.HandleAsync(
            new AdministratorLoginCommand("unknown@example.com", "anything"), CancellationToken.None);
        var wrongPasswordResult = await wrongPasswordFixture.Handler.HandleAsync(
            new AdministratorLoginCommand("admin@example.com", "wrong-password"), CancellationToken.None);

        Assert.Equal(unknownResult.Error.Code, wrongPasswordResult.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_SucceedsForACorrectActiveLogin_AndIssuesBothTokens()
    {
        var fixture = NewFixture();
        var administrator = SeedAdministrator(fixture.Administrators, fixture.PasswordHasher);

        var result = await fixture.Handler.HandleAsync(
            new AdministratorLoginCommand("admin@example.com", "correct-password-123"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrEmpty(result.Value.AccessToken));
        Assert.False(string.IsNullOrEmpty(result.Value.RefreshToken));
        Assert.Single(fixture.RefreshTokens.Added);
        Assert.Equal(administrator.Id, fixture.RefreshTokens.Added[0].AdministratorId);
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount);

        var entry = Assert.Single(fixture.AuditWriter.Entries);
        Assert.Equal("administrator.login_succeeded", entry.Action);
        Assert.Equal(administrator.Id.Value, entry.AdministratorId);
    }

    [Fact]
    public async Task HandleAsync_RecordsTheLoginTimestamp()
    {
        var fixture = NewFixture();
        var administrator = SeedAdministrator(fixture.Administrators, fixture.PasswordHasher);

        await fixture.Handler.HandleAsync(
            new AdministratorLoginCommand("admin@example.com", "correct-password-123"),
            CancellationToken.None);

        Assert.Equal(Now, administrator.LastLoginAt);
    }

    [Fact]
    public async Task HandleAsync_IsCaseInsensitiveOnEmail()
    {
        var fixture = NewFixture();
        SeedAdministrator(fixture.Administrators, fixture.PasswordHasher);

        var result = await fixture.Handler.HandleAsync(
            new AdministratorLoginCommand("ADMIN@EXAMPLE.COM", "correct-password-123"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_RejectsAWrongPassword_BelowTheLockoutThreshold_IncrementsTheCounter_WithoutLockingOut()
    {
        var fixture = NewFixture();
        var administrator = SeedAdministrator(fixture.Administrators, fixture.PasswordHasher);

        for (var i = 0; i < 4; i++)
        {
            var result = await fixture.Handler.HandleAsync(
                new AdministratorLoginCommand("admin@example.com", "wrong-password"), CancellationToken.None);
            Assert.Equal("administrator.invalid_credentials", result.Error.Code);
        }

        Assert.Equal(4, administrator.FailedLoginAttempts);
        Assert.False(administrator.IsLockedOut(Now));
        Assert.Equal(4, fixture.AuditWriter.Entries.Count);
        Assert.All(fixture.AuditWriter.Entries, e => Assert.Equal("administrator.login_failed", e.Action));
    }

    [Fact]
    public async Task HandleAsync_OnTheFifthConsecutiveWrongPassword_LocksTheAccount_AndAuditsOnlyTheLockout()
    {
        var fixture = NewFixture();
        var administrator = SeedAdministrator(fixture.Administrators, fixture.PasswordHasher);

        for (var i = 0; i < 4; i++)
        {
            await fixture.Handler.HandleAsync(
                new AdministratorLoginCommand("admin@example.com", "wrong-password"), CancellationToken.None);
        }

        var result = await fixture.Handler.HandleAsync(
            new AdministratorLoginCommand("admin@example.com", "wrong-password"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("administrator.locked_out", result.Error.Code);
        Assert.True(administrator.IsLockedOut(Now));

        // The triggering attempt itself gets only the lockout action, not a redundant
        // login_failed entry for the same event (Stage 10, section 4).
        Assert.Equal(5, fixture.AuditWriter.Entries.Count);
        Assert.Equal("administrator.login_failed", fixture.AuditWriter.Entries[0].Action);
        Assert.Equal("administrator.login_failed", fixture.AuditWriter.Entries[3].Action);
        Assert.Equal("administrator.locked_out", fixture.AuditWriter.Entries[4].Action);
    }

    [Fact]
    public async Task HandleAsync_DuringAnActiveLockout_RejectsAWrongPassword_WithoutExtendingOrEscalating()
    {
        var fixture = NewFixture();
        var administrator = SeedAdministrator(fixture.Administrators, fixture.PasswordHasher);
        for (var i = 0; i < 5; i++)
        {
            await fixture.Handler.HandleAsync(
                new AdministratorLoginCommand("admin@example.com", "wrong-password"), CancellationToken.None);
        }
        var lockedUntilBefore = administrator.LockedUntil;

        var result = await fixture.Handler.HandleAsync(
            new AdministratorLoginCommand("admin@example.com", "wrong-password"), CancellationToken.None);

        Assert.Equal("administrator.locked_out", result.Error.Code);
        Assert.Equal(lockedUntilBefore, administrator.LockedUntil); // unchanged -- not extended.
        Assert.Equal(1, administrator.ConsecutiveLockouts); // unchanged -- not escalated.

        var lastEntry = fixture.AuditWriter.Entries[^1];
        Assert.Equal("administrator.login_failed", lastEntry.Action); // ongoing block, not a new trigger.
        Assert.Contains("administrator.locked_out", lastEntry.AfterData!);
    }

    [Fact]
    public async Task HandleAsync_DuringAnActiveLockout_RejectsEvenTheCorrectPassword()
    {
        var fixture = NewFixture();
        var administrator = SeedAdministrator(fixture.Administrators, fixture.PasswordHasher);
        for (var i = 0; i < 5; i++)
        {
            await fixture.Handler.HandleAsync(
                new AdministratorLoginCommand("admin@example.com", "wrong-password"), CancellationToken.None);
        }

        var result = await fixture.Handler.HandleAsync(
            new AdministratorLoginCommand("admin@example.com", "correct-password-123"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("administrator.locked_out", result.Error.Code);
        Assert.Null(administrator.LastLoginAt); // never reached RecordLogin.
    }

    [Fact]
    public async Task HandleAsync_OnSuccess_FullyResetsPriorLockoutBookkeeping()
    {
        var fixture = NewFixture();
        var administrator = SeedAdministrator(fixture.Administrators, fixture.PasswordHasher);
        for (var i = 0; i < 3; i++)
        {
            await fixture.Handler.HandleAsync(
                new AdministratorLoginCommand("admin@example.com", "wrong-password"), CancellationToken.None);
        }
        Assert.Equal(3, administrator.FailedLoginAttempts);

        var result = await fixture.Handler.HandleAsync(
            new AdministratorLoginCommand("admin@example.com", "correct-password-123"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, administrator.FailedLoginAttempts);
        Assert.Equal(0, administrator.ConsecutiveLockouts);
        Assert.Null(administrator.LockedUntil);
    }
}
