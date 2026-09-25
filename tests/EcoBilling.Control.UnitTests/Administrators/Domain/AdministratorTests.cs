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
    public void Create_StartsWithNoLockoutBookkeeping()
    {
        var administrator = NewAdministrator();

        Assert.Equal(0, administrator.FailedLoginAttempts);
        Assert.Equal(0, administrator.ConsecutiveLockouts);
        Assert.Null(administrator.LockedUntil);
        Assert.Null(administrator.LastFailedLoginAt);
        Assert.False(administrator.IsLockedOut(Now));
    }

    [Fact]
    public void RecordFailedLoginAttempt_BelowThreshold_IncrementsCounter_WithoutLockingOut()
    {
        var administrator = NewAdministrator();

        for (var i = 0; i < 4; i++)
        {
            var triggered = administrator.RecordFailedLoginAttempt(Later.AddSeconds(i));
            Assert.False(triggered);
        }

        Assert.Equal(4, administrator.FailedLoginAttempts);
        Assert.Equal(0, administrator.ConsecutiveLockouts);
        Assert.False(administrator.IsLockedOut(Later));
    }

    [Fact]
    public void RecordFailedLoginAttempt_OnTheFifthConsecutiveFailure_TriggersALockout_AndReturnsTrue()
    {
        var administrator = NewAdministrator();

        for (var i = 0; i < 4; i++)
        {
            administrator.RecordFailedLoginAttempt(Later.AddSeconds(i));
        }

        var triggered = administrator.RecordFailedLoginAttempt(Later.AddSeconds(4));

        Assert.True(triggered);
        Assert.Equal(1, administrator.ConsecutiveLockouts);
        Assert.Equal(0, administrator.FailedLoginAttempts); // reset -- the next lockout needs its own fresh 5 failures.
        Assert.True(administrator.IsLockedOut(Later.AddSeconds(4)));
        Assert.Equal(Later.AddSeconds(4).AddMinutes(1), administrator.LockedUntil);
    }

    [Fact]
    public void RecordFailedLoginAttempt_WhileAlreadyLockedOut_IsANoOp_AndReturnsFalse()
    {
        // Deliberate (Stage 10, section 2): an attacker hammering the endpoint during an
        // active lockout must not be able to extend it or advance escalation further.
        var administrator = NewAdministrator();
        Fail(administrator, times: 5, at: Later); // triggers the first lockout (1 minute).
        var lockedUntilBefore = administrator.LockedUntil;

        var triggered = administrator.RecordFailedLoginAttempt(Later.AddSeconds(30)); // still within the 1-minute lock.

        Assert.False(triggered);
        Assert.Equal(lockedUntilBefore, administrator.LockedUntil); // unchanged -- not extended.
        Assert.Equal(1, administrator.ConsecutiveLockouts); // unchanged -- not escalated.
    }

    [Fact]
    public void RecordFailedLoginAttempt_EscalatesDuration_OneTwoFourEightSixteenThenCapsAtThirty()
    {
        var administrator = NewAdministrator();
        var expectedMinutes = new[] { 1, 2, 4, 8, 16, 30, 30 };
        var now = Later;

        foreach (var expected in expectedMinutes)
        {
            now += TimeSpan.FromMinutes(expected) + TimeSpan.FromSeconds(1); // past the current lockout, if any.
            Fail(administrator, times: 5, at: now);

            Assert.Equal(now.AddMinutes(expected), administrator.LockedUntil);

            now = administrator.LockedUntil!.Value;
        }
    }

    [Fact]
    public void RecordFailedLoginAttempt_AfterTheEscalationResetWindow_RestartsAtTierOne()
    {
        var administrator = NewAdministrator();
        Fail(administrator, times: 5, at: Later); // first lockout: tier 1, 1 minute.
        var afterFirstLockout = administrator.LockedUntil!.Value;

        // More than 24 hours pass with no further failed attempts.
        var muchLater = afterFirstLockout.AddHours(25);
        Fail(administrator, times: 5, at: muchLater);

        Assert.Equal(1, administrator.ConsecutiveLockouts); // back to tier 1, not tier 2.
        Assert.Equal(muchLater.AddMinutes(1), administrator.LockedUntil);
    }

    [Fact]
    public void RecordFailedLoginAttempt_WithinTheEscalationResetWindow_KeepsEscalating()
    {
        var administrator = NewAdministrator();
        Fail(administrator, times: 5, at: Later); // tier 1, 1 minute.
        var afterFirstLockout = administrator.LockedUntil!.Value;

        var soonAfter = afterFirstLockout.AddHours(1); // well within 24 hours.
        Fail(administrator, times: 5, at: soonAfter);

        Assert.Equal(2, administrator.ConsecutiveLockouts); // escalated, not reset.
    }

    [Fact]
    public void RecordLogin_FullyResetsLockoutBookkeeping()
    {
        var administrator = NewAdministrator();
        for (var i = 0; i < 3; i++)
        {
            administrator.RecordFailedLoginAttempt(Later.AddSeconds(i));
        }

        administrator.RecordLogin(Later.AddMinutes(1));

        Assert.Equal(0, administrator.FailedLoginAttempts);
        Assert.Equal(0, administrator.ConsecutiveLockouts);
        Assert.Null(administrator.LockedUntil);
        Assert.Null(administrator.LastFailedLoginAt);
    }

    [Fact]
    public void IsLockedOut_ReturnsFalseOnceLockedUntilHasPassed()
    {
        var administrator = NewAdministrator();
        Fail(administrator, times: 5, at: Later); // tier 1, 1 minute.

        Assert.True(administrator.IsLockedOut(administrator.LockedUntil!.Value.AddSeconds(-1)));
        Assert.False(administrator.IsLockedOut(administrator.LockedUntil!.Value));
    }

    /// <summary>
    /// Records <paramref name="times"/> failed attempts at the exact same instant --
    /// deliberately not spaced apart, so a test asserting an exact <c>LockedUntil</c>
    /// value against <paramref name="at"/> doesn't have to account for a drifting
    /// timestamp across the batch.
    /// </summary>
    private static void Fail(Administrator administrator, int times, DateTimeOffset at)
    {
        for (var i = 0; i < times; i++)
        {
            administrator.RecordFailedLoginAttempt(at);
        }
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
