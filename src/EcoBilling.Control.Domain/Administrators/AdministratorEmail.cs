using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.Domain.Administrators;

/// <summary>An administrator's email address, normalized for lookup.</summary>
public sealed class AdministratorEmail : IEquatable<AdministratorEmail>
{
    public const int MaxInputLength = 320; // RFC 5321's own practical upper bound.

    private AdministratorEmail(string original, string normalized)
    {
        Original = original;
        Normalized = normalized;
    }

    /// <summary>The address as entered, for display.</summary>
    public string Original { get; }

    /// <summary>The address the unique index and every lookup are built on.</summary>
    public string Normalized { get; }

    public static Result<AdministratorEmail> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > MaxInputLength)
        {
            return Failure();
        }

        var original = input.Trim();

        // Deliberately not attempting a "correct" RFC 5322 email regex -- no simple
        // pattern gets that right, and an overly strict one rejects valid addresses.
        // This is a pragmatic shape check: exactly one '@', a non-empty local part, and
        // a domain part containing at least one '.'.
        var atIndex = original.IndexOf('@');

        if (atIndex <= 0 || atIndex != original.LastIndexOf('@') || atIndex == original.Length - 1)
        {
            return Failure();
        }

        var domain = original[(atIndex + 1)..];

        if (!domain.Contains('.') || domain.StartsWith('.') || domain.EndsWith('.'))
        {
            return Failure();
        }

        return Result.Success(new AdministratorEmail(original, Normalize(original)));
    }

    /// <summary>
    /// The single normalization rule for administrator emails: trim, then lower-case
    /// using the invariant culture -- the same rule <c>DistrictCode</c> uses for the same
    /// reason (Stage 1): a culture-sensitive case conversion under a Turkish or Azeri
    /// locale would map some characters differently, silently producing a value the
    /// unique index could never match.
    /// </summary>
    public static string Normalize(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return input.Trim().ToLowerInvariant();
    }

    private static Result<AdministratorEmail> Failure() =>
        Result.Failure<AdministratorEmail>(AdministratorErrors.InvalidEmail);

    public bool Equals(AdministratorEmail? other) =>
        other is not null && string.Equals(Normalized, other.Normalized, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as AdministratorEmail);

    public override int GetHashCode() => Normalized.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Normalized;
}
