using System.Text.RegularExpressions;
using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.Domain.Districts;

/// <summary>
/// A district routing code, for example <c>BISHKEK-01</c>.
/// The code is a router, not a credential: it is deliberately human-readable and
/// therefore guessable. Brute-force protection is rate limiting on the public
/// resolve endpoint, not opacity of the code.
/// </summary>
public sealed partial class DistrictCode : IEquatable<DistrictCode>
{
    /// <summary>Upper bound on accepted input, applied before any pattern matching.</summary>
    public const int MaxInputLength = 64;

    private DistrictCode(string original, string normalized)
    {
        Original = original;
        Normalized = normalized;
    }

    /// <summary>The code as the administrator typed it, kept for display.</summary>
    public string Original { get; }

    /// <summary>The code the unique index and every lookup are built on.</summary>
    public string Normalized { get; }

    public static Result<DistrictCode> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > MaxInputLength)
        {
            return Result.Failure<DistrictCode>(DistrictErrors.InvalidCode);
        }

        var original = input.Trim();
        var normalized = Normalize(original);

        return Pattern().IsMatch(normalized)
            ? Result.Success(new DistrictCode(original, normalized))
            : Result.Failure<DistrictCode>(DistrictErrors.InvalidCode);
    }

    /// <summary>
    /// The single normalization rule for district codes: trim, then upper-case using the
    /// invariant culture. Exposed so a lookup can normalize an input without constructing
    /// a value object, guaranteeing writes and reads agree with the unique index.
    /// </summary>
    /// <remarks>
    /// The invariant culture is mandatory. A culture-sensitive upper-casing maps
    /// <c>i</c> to <c>I-with-dot</c> under Turkish and Azeri locales, which would
    /// silently produce a value the unique index can never match.
    /// </remarks>
    public static string Normalize(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return input.Trim().ToUpperInvariant();
    }

    // [0-9] rather than \d on purpose: \d also matches non-ASCII decimal digits such as
    // Arabic-Indic numerals, which would let visually different codes past validation.
    [GeneratedRegex("^[A-Z]{2,10}-[0-9]{2,4}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public bool Equals(DistrictCode? other) =>
        other is not null && string.Equals(Normalized, other.Normalized, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as DistrictCode);

    public override int GetHashCode() => Normalized.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Normalized;
}
