namespace EcoBilling.Control.Domain.Administrators;

/// <summary>
/// The result of issuing a new <see cref="RefreshToken"/>: the entity to persist
/// (holding only the hash) alongside the one-time raw value to hand to the client.
/// The raw value is never itself persisted or logged anywhere.
/// </summary>
public sealed record IssuedRefreshToken(RefreshToken Token, string RawValue);
