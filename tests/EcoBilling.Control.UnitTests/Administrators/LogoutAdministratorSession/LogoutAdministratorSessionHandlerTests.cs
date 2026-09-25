using EcoBilling.Control.Application.Administrators.LogoutAdministratorSession;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.UnitTests.TestDoubles;

namespace EcoBilling.Control.UnitTests.Administrators.LogoutAdministratorSession;

public sealed class LogoutAdministratorSessionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    private static LogoutAdministratorSessionHandler NewHandler(FakeRefreshTokenRepository refreshTokens, FakeUnitOfWork unitOfWork) =>
        new(refreshTokens, unitOfWork, new FixedClock(Now));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task HandleAsync_SucceedsAsANoOp_ForAMissingToken(string? token)
    {
        var refreshTokens = new FakeRefreshTokenRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = NewHandler(refreshTokens, unitOfWork);

        var result = await handler.HandleAsync(new LogoutAdministratorSessionCommand(token), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_SucceedsAsANoOp_ForATokenThatWasNeverIssued()
    {
        var refreshTokens = new FakeRefreshTokenRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = NewHandler(refreshTokens, unitOfWork);

        var result = await handler.HandleAsync(
            new LogoutAdministratorSessionCommand("a-value-nobody-ever-issued"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_RevokesExactlyThePresentedToken()
    {
        var refreshTokens = new FakeRefreshTokenRepository();
        var issued = RefreshToken.IssueNew(RefreshTokenId.New(), AdministratorId.New(), Now, Lifetime);
        refreshTokens.Seed(issued.Token);
        var unitOfWork = new FakeUnitOfWork();
        var handler = NewHandler(refreshTokens, unitOfWork);

        var result = await handler.HandleAsync(new LogoutAdministratorSessionCommand(issued.RawValue), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(issued.Token.RevokedAt);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_SucceedsAsANoOp_ForAnAlreadyRevokedToken_WithoutRevokingAgain()
    {
        var refreshTokens = new FakeRefreshTokenRepository();
        var issued = RefreshToken.IssueNew(RefreshTokenId.New(), AdministratorId.New(), Now, Lifetime);
        issued.Token.Revoke(Now.AddMinutes(-1));
        refreshTokens.Seed(issued.Token);
        var unitOfWork = new FakeUnitOfWork();
        var handler = NewHandler(refreshTokens, unitOfWork);

        var result = await handler.HandleAsync(new LogoutAdministratorSessionCommand(issued.RawValue), CancellationToken.None);

        Assert.True(result.IsSuccess); // idempotent -- would fail if this called Revoke() again (Domain rejects double-revoke)
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_DoesNotRevokeOtherSessionsForTheSameAdministrator()
    {
        var refreshTokens = new FakeRefreshTokenRepository();
        var administratorId = AdministratorId.New();
        var toLogOut = RefreshToken.IssueNew(RefreshTokenId.New(), administratorId, Now, Lifetime);
        var otherSession = RefreshToken.IssueNew(RefreshTokenId.New(), administratorId, Now, Lifetime);
        refreshTokens.Seed(toLogOut.Token);
        refreshTokens.Seed(otherSession.Token);
        var unitOfWork = new FakeUnitOfWork();
        var handler = NewHandler(refreshTokens, unitOfWork);

        await handler.HandleAsync(new LogoutAdministratorSessionCommand(toLogOut.RawValue), CancellationToken.None);

        Assert.True(otherSession.Token.IsActive(Now));
    }
}
