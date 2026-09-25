namespace EcoBilling.Control.Application.Abstractions;

/// <summary>
/// Hashes and verifies administrator passwords (Q11: PBKDF2 via ASP.NET Core Identity's
/// built-in <c>PasswordHasher&lt;TUser&gt;</c>).
/// </summary>
/// <remarks>
/// Deliberately takes and returns plain strings, not a domain or ASP.NET Core Identity
/// type: <c>PasswordHasher&lt;TUser&gt;</c>'s API requires a <c>TUser</c> instance that
/// its own default implementation never actually uses, and threading that requirement
/// through this interface would leak a third-party library's shape into
/// Application.Abstractions. Infrastructure's implementation satisfies that requirement
/// internally, where depending on ASP.NET Core Identity is allowed.
/// </remarks>
public interface IPasswordHasher
{
    string Hash(string plainPassword);

    bool Verify(string hash, string plainPassword);

    /// <summary>
    /// A precomputed, valid-format hash of a fixed, arbitrary password, no real
    /// administrator's. Used only so login's response time stays the same when no real
    /// stored hash exists to compare against (an unknown email) as when one does --
    /// otherwise the time to reject the attempt would itself reveal whether the email
    /// exists, independent of the response body. The implementation computes this once
    /// (it never changes at runtime), not on every call.
    /// </summary>
    string DummyHash { get; }
}
