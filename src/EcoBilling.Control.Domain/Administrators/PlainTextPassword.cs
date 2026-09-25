using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.Domain.Administrators;

/// <summary>
/// An unhashed password, validated only for the rules that apply when a password is
/// being *set* (NIST SP 800-63B baseline, Q10): a minimum length, no forced complexity,
/// no forced periodic rotation. Breach-list checking (e.g. HaveIBeenPwned) is a deferred
/// future enhancement, not built into this rule -- it is an external network dependency
/// and was deliberately left out of this stage.
/// </summary>
/// <remarks>
/// Used only where a new password is accepted (the provisioning tool). Login does not
/// construct this type at all: a wrong-length string attempting to log in must fail
/// through the exact same path as any other bad guess, not a distinguishable
/// "too short" error, so length is never checked at login time.
/// </remarks>
public sealed class PlainTextPassword
{
    public const int MinLength = 12;

    private readonly string _value;

    private PlainTextPassword(string value) => _value = value;

    public static Result<PlainTextPassword> Create(string? input)
    {
        if (string.IsNullOrEmpty(input) || input.Length < MinLength)
        {
            return Result.Failure<PlainTextPassword>(AdministratorErrors.InvalidPassword);
        }

        return Result.Success(new PlainTextPassword(input));
    }

    /// <summary>The raw value, for the one call site that hashes it. Never logged, never stored.</summary>
    public string Reveal() => _value;

    // Deliberately does NOT return _value: a stray Console.WriteLine(password) or a
    // structured-logging call that interpolates this object must not leak the secret.
    public override string ToString() => "[REDACTED]";
}
