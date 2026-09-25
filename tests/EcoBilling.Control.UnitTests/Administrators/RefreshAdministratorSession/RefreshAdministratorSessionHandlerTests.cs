using EcoBilling.Control.Application.Administrators.RefreshAdministratorSession;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.UnitTests.TestDoubles;

namespace EcoBilling.Control.UnitTests.Administrators.RefreshAdministratorSession;

public sealed class RefreshAdministratorSessionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    private sealed record Fixture(
        Administrator Administrator,
        FakeAdministratorRepository Administrators,
        FakeRefreshTokenRepository RefreshTokens,
        FakeUnitOfWork UnitOfWork,
        RefreshAdministratorSessionHandler Handler);

    private static Fixture NewFixture(DateTimeOffset now)
    {
        var administrator = Administrator.Create(
            AdministratorId.New(),
            AdministratorEmail.Create("admin@example.com").Value,
            "Ada Lovelace",
            "irrelevant-hash",
            now).Value;

        var administrators = new FakeAdministratorRepository();
        administrators.Seed(administrator);
        var refreshTokens = new FakeRefreshTokenRepository();
        var unitOfWork = new FakeUnitOfWork();
        var accessTokenIssuer = new FakeAccessTokenIssuer(TimeSpan.FromMinutes(15));
        var handler = new RefreshAdministratorSessionHandler(
            administrators, refreshTokens, accessTokenIssuer, unitOfWork, new FixedClock(now));

        return new Fixture(administrator, administrators, refreshTokens, unitOfWork, handler);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task HandleAsync_RejectsAMissingToken(string? token)
    {
        var fixture = NewFixture(Now);

        var result = await fixture.Handler.HandleAsync(new RefreshAdministratorSessionCommand(token), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("administrator.invalid_refresh_token", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_RejectsATokenThatWasNeverIssued()
    {
        var fixture = NewFixture(Now);

        var result = await fixture.Handler.HandleAsync(
            new RefreshAdministratorSessionCommand("a-value-nobody-ever-issued"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("administrator.invalid_refresh_token", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_RotatesAValidToken_RevokingTheOldOneAndIssuingANewOne()
    {
        var fixture = NewFixture(Now);
        var issued = RefreshToken.IssueNew(RefreshTokenId.New(), fixture.Administrator.Id, Now, Lifetime);
        fixture.RefreshTokens.Seed(issued.Token);

        var result = await fixture.Handler.HandleAsync(
            new RefreshAdministratorSessionCommand(issued.RawValue),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(issued.RawValue, result.Value.RefreshToken);
        Assert.NotNull(issued.Token.RevokedAt);
        Assert.Single(fixture.RefreshTokens.Added); // exactly the new token -- the old one was seeded directly, not "Added".
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_RejectsAnExpiredButNeverRevokedToken_WithoutTouchingOtherSessions()
    {
        // An ordinary stale token, not a reuse signal -- must not trigger revoke-all.
        var fixture = NewFixture(Now);
        var expired = RefreshToken.IssueNew(RefreshTokenId.New(), fixture.Administrator.Id, Now, TimeSpan.FromMinutes(1));
        fixture.RefreshTokens.Seed(expired.Token);
        var stillActive = RefreshToken.IssueNew(RefreshTokenId.New(), fixture.Administrator.Id, Now, Lifetime);
        fixture.RefreshTokens.Seed(stillActive.Token);

        var afterExpiry = Now.AddMinutes(5);
        var lateFixture = NewFixtureAt(fixture, afterExpiry);

        var result = await lateFixture.Handler.HandleAsync(
            new RefreshAdministratorSessionCommand(expired.RawValue),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("administrator.invalid_refresh_token", result.Error.Code);
        Assert.True(stillActive.Token.IsActive(afterExpiry)); // untouched
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_ReuseOfAnAlreadyRotatedOutToken_RevokesEveryOtherActiveSession()
    {
        var fixture = NewFixture(Now);

        // Simulate a normal, legitimate rotation first.
        var original = RefreshToken.IssueNew(RefreshTokenId.New(), fixture.Administrator.Id, Now, Lifetime);
        fixture.RefreshTokens.Seed(original.Token);
        var afterFirstRefresh = Now.AddMinutes(1);
        var firstRefreshFixture = NewFixtureAt(fixture, afterFirstRefresh);
        var firstResult = await firstRefreshFixture.Handler.HandleAsync(
            new RefreshAdministratorSessionCommand(original.RawValue), CancellationToken.None);
        Assert.True(firstResult.IsSuccess);

        // A second, unrelated active session for the same administrator (e.g. a
        // different device) that a theft response should also revoke.
        var otherDeviceSession = RefreshToken.IssueNew(RefreshTokenId.New(), fixture.Administrator.Id, afterFirstRefresh, Lifetime);
        fixture.RefreshTokens.Seed(otherDeviceSession.Token);

        // Now the ORIGINAL (already-rotated-out) token is replayed.
        var replayAttemptAt = afterFirstRefresh.AddMinutes(1);
        var replayFixture = NewFixtureAt(fixture, replayAttemptAt);

        var replayResult = await replayFixture.Handler.HandleAsync(
            new RefreshAdministratorSessionCommand(original.RawValue), CancellationToken.None);

        Assert.True(replayResult.IsFailure);
        Assert.Equal("administrator.invalid_refresh_token", replayResult.Error.Code);
        Assert.False(otherDeviceSession.Token.IsActive(replayAttemptAt)); // revoked as a side effect of the reuse.
    }

    [Fact]
    public async Task HandleAsync_RejectsRefreshForAnAdministratorThatNoLongerExists()
    {
        var administrators = new FakeAdministratorRepository(); // deliberately empty
        var refreshTokens = new FakeRefreshTokenRepository();
        var missingAdministratorId = AdministratorId.New();
        var issued = RefreshToken.IssueNew(RefreshTokenId.New(), missingAdministratorId, Now, Lifetime);
        refreshTokens.Seed(issued.Token);
        var unitOfWork = new FakeUnitOfWork();
        var handler = new RefreshAdministratorSessionHandler(
            administrators, refreshTokens, new FakeAccessTokenIssuer(TimeSpan.FromMinutes(15)), unitOfWork, new FixedClock(Now));

        var result = await handler.HandleAsync(new RefreshAdministratorSessionCommand(issued.RawValue), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("administrator.invalid_refresh_token", result.Error.Code);
    }

    // Builds a handler sharing the same repositories/administrator as `fixture`, but
    // driven by a clock fixed at `now` -- lets a test simulate "some time has passed"
    // between two calls without needing a mutable clock double.
    private static Fixture NewFixtureAt(Fixture fixture, DateTimeOffset now)
    {
        var handler = new RefreshAdministratorSessionHandler(
            fixture.Administrators,
            fixture.RefreshTokens,
            new FakeAccessTokenIssuer(TimeSpan.FromMinutes(15)),
            fixture.UnitOfWork,
            new FixedClock(now));

        return fixture with { Handler = handler };
    }
}
