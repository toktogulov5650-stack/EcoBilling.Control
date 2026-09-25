using EcoBilling.Control.Domain.Administrators;

namespace EcoBilling.Control.UnitTests.Administrators.Domain;

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    [Fact]
    public void IssueNew_ProducesAnActiveTokenAndARawValue()
    {
        var issued = RefreshToken.IssueNew(RefreshTokenId.New(), AdministratorId.New(), Now, Lifetime);

        Assert.True(issued.Token.IsActive(Now));
        Assert.Equal(Now + Lifetime, issued.Token.ExpiresAt);
        Assert.False(string.IsNullOrEmpty(issued.RawValue));
        Assert.Null(issued.Token.RevokedAt);
    }

    [Fact]
    public void IssueNew_NeverPersistsTheRawValue_OnlyItsHash()
    {
        var issued = RefreshToken.IssueNew(RefreshTokenId.New(), AdministratorId.New(), Now, Lifetime);

        Assert.NotEqual(issued.RawValue, issued.Token.TokenHash);
        Assert.Equal(RefreshToken.HashRawValue(issued.RawValue), issued.Token.TokenHash);
    }

    [Fact]
    public void IssueNew_ProducesADifferentRawValueEveryTime()
    {
        var first = RefreshToken.IssueNew(RefreshTokenId.New(), AdministratorId.New(), Now, Lifetime);
        var second = RefreshToken.IssueNew(RefreshTokenId.New(), AdministratorId.New(), Now, Lifetime);

        Assert.NotEqual(first.RawValue, second.RawValue);
        Assert.NotEqual(first.Token.TokenHash, second.Token.TokenHash);
    }

    [Fact]
    public void HashRawValue_IsDeterministic()
    {
        var issued = RefreshToken.IssueNew(RefreshTokenId.New(), AdministratorId.New(), Now, Lifetime);

        // A caller presenting the same raw value back must hash to the same value the
        // repository can look up by -- this is the whole mechanism refresh/logout rely on.
        Assert.Equal(RefreshToken.HashRawValue(issued.RawValue), RefreshToken.HashRawValue(issued.RawValue));
    }

    [Fact]
    public void IsActive_IsFalseOnceExpired()
    {
        var issued = RefreshToken.IssueNew(RefreshTokenId.New(), AdministratorId.New(), Now, Lifetime);

        Assert.False(issued.Token.IsActive(Now + Lifetime));
        Assert.False(issued.Token.IsActive(Now + Lifetime + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Revoke_MarksTheTokenInactiveAndStampsTheTime()
    {
        var issued = RefreshToken.IssueNew(RefreshTokenId.New(), AdministratorId.New(), Now, Lifetime);
        var revokedAt = Now.AddHours(1);

        var result = issued.Token.Revoke(revokedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(revokedAt, issued.Token.RevokedAt);
        Assert.False(issued.Token.IsActive(revokedAt));
        Assert.Null(issued.Token.ReplacedByTokenId);
    }

    [Fact]
    public void Revoke_CanRecordWhatReplacedIt()
    {
        var issued = RefreshToken.IssueNew(RefreshTokenId.New(), AdministratorId.New(), Now, Lifetime);
        var replacementId = RefreshTokenId.New();

        issued.Token.Revoke(Now.AddHours(1), replacementId);

        Assert.Equal(replacementId, issued.Token.ReplacedByTokenId);
    }

    [Fact]
    public void Revoke_OnAnAlreadyRevokedTokenIsRejected()
    {
        // Strict, like District.Activate/Deactivate (Stage 1): callers that need
        // idempotent revocation of a whole set filter to IsActive first, at the
        // Application layer (RefreshAdministratorSessionHandler's reuse detection).
        var issued = RefreshToken.IssueNew(RefreshTokenId.New(), AdministratorId.New(), Now, Lifetime);
        issued.Token.Revoke(Now.AddHours(1));

        var result = issued.Token.Revoke(Now.AddHours(2));

        Assert.True(result.IsFailure);
        Assert.Equal("administrator.invalid_refresh_token", result.Error.Code);
    }

    [Fact]
    public void IssueNew_RejectsAnEmptyIdAsAProgrammingError()
    {
        Assert.Throws<ArgumentException>(() =>
            RefreshToken.IssueNew(RefreshTokenId.Empty, AdministratorId.New(), Now, Lifetime));
    }

    [Fact]
    public void IssueNew_RejectsAnEmptyAdministratorIdAsAProgrammingError()
    {
        Assert.Throws<ArgumentException>(() =>
            RefreshToken.IssueNew(RefreshTokenId.New(), AdministratorId.Empty, Now, Lifetime));
    }

    [Fact]
    public void IssueNew_RejectsANonPositiveLifetimeAsAProgrammingError()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RefreshToken.IssueNew(RefreshTokenId.New(), AdministratorId.New(), Now, TimeSpan.Zero));
    }
}
