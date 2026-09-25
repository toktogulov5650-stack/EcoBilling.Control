using EcoBilling.Control.Infrastructure.Authentication;

namespace EcoBilling.Control.IntegrationTests.Authentication;

/// <summary>
/// Verifies the real PBKDF2 wrapper (Q11). No PostgreSQL needed -- this is pure,
/// in-process cryptography.
/// </summary>
public sealed class PasswordHasherTests
{
    [Fact]
    public void Hash_ThenVerify_Succeeds()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("a-correct-password");

        Assert.True(hasher.Verify(hash, "a-correct-password"));
    }

    [Fact]
    public void Verify_FailsForAWrongPassword()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("a-correct-password");

        Assert.False(hasher.Verify(hash, "a-different-password"));
    }

    [Fact]
    public void Hash_ProducesADifferentValueEveryTime_ForTheSamePassword()
    {
        // PBKDF2 salts each hash independently -- two hashes of the same password must
        // never be equal, even though both verify successfully against it.
        var hasher = new PasswordHasher();

        var first = hasher.Hash("a-correct-password");
        var second = hasher.Hash("a-correct-password");

        Assert.NotEqual(first, second);
        Assert.True(hasher.Verify(first, "a-correct-password"));
        Assert.True(hasher.Verify(second, "a-correct-password"));
    }

    [Fact]
    public void DummyHash_IsAValidHashThatVerifiesAgainstNoRealPassword()
    {
        var hasher = new PasswordHasher();

        Assert.False(string.IsNullOrEmpty(hasher.DummyHash));
        Assert.False(hasher.Verify(hasher.DummyHash, "any-guess-at-all"));
    }

    [Fact]
    public void DummyHash_IsComputedOnceAndStaysStableAcrossCalls()
    {
        var hasher = new PasswordHasher();

        Assert.Equal(hasher.DummyHash, hasher.DummyHash);
    }
}
