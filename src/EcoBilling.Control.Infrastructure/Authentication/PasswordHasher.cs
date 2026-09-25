using EcoBilling.Control.Application.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace EcoBilling.Control.Infrastructure.Authentication;

/// <summary>PBKDF2-backed <see cref="IPasswordHasher"/> (Q11): ASP.NET Core Identity's built-in <c>PasswordHasher&lt;TUser&gt;</c>.</summary>
/// <remarks>
/// <c>PasswordHasher&lt;TUser&gt;</c>'s API requires a <c>TUser</c> instance on every
/// call that its default implementation never actually reads; a private, unused marker
/// type satisfies that requirement here so the detail never crosses into
/// <see cref="IPasswordHasher"/>'s clean, plain-string interface.
///
/// Registered as a singleton (Api composition root): stateless, and
/// <see cref="DummyHash"/> must be computed exactly once per process, not per request.
/// </remarks>
public sealed class PasswordHasher : IPasswordHasher
{
    private sealed class UnusedUserMarker;

    private static readonly UnusedUserMarker Marker = new();

    private readonly Microsoft.AspNetCore.Identity.PasswordHasher<UnusedUserMarker> _hasher = new();

    public PasswordHasher() =>
        DummyHash = _hasher.HashPassword(Marker, "dummy-password-for-timing-safety-only-never-a-real-account");

    public string DummyHash { get; }

    public string Hash(string plainPassword) => _hasher.HashPassword(Marker, plainPassword);

    public bool Verify(string hash, string plainPassword) =>
        _hasher.VerifyHashedPassword(Marker, hash, plainPassword) != PasswordVerificationResult.Failed;
}
