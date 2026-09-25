using System.Security.Cryptography;
using System.Text;
using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.Domain.Administrators;

/// <summary>
/// A single refresh-token session, tracked separately from <see cref="Administrator"/>
/// (not embedded in it) so that reuse detection can revoke every other active session
/// independently of the one currently being rotated.
/// </summary>
/// <remarks>
/// <see cref="TokenHash"/> is a fast cryptographic hash (SHA-256) of a 256-bit random
/// value, deliberately not the same PBKDF2 mechanism used for administrator passwords:
/// PBKDF2's slowness exists to resist brute-forcing a low-entropy, human-chosen secret,
/// which a refresh token is not -- it has no guessable structure, so a slow hash buys
/// nothing here and would waste CPU on every refresh call.
/// </remarks>
public sealed class RefreshToken
{
    private RefreshToken(
        RefreshTokenId id,
        AdministratorId administratorId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        AdministratorId = administratorId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>For EF Core materialization only (Stage 4's pattern) -- never called by application code.</summary>
    private RefreshToken()
    {
    }

    public RefreshTokenId Id { get; }

    public AdministratorId AdministratorId { get; }

    public string TokenHash { get; } = null!;

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public RefreshTokenId? ReplacedByTokenId { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    /// <summary>
    /// Generates a new, cryptographically random raw token and the entity that stores
    /// only its hash. The raw value is returned exactly once -- it cannot be recovered
    /// from the persisted entity afterward.
    /// </summary>
    public static IssuedRefreshToken IssueNew(
        RefreshTokenId id,
        AdministratorId administratorId,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        if (id == RefreshTokenId.Empty)
        {
            throw new ArgumentException("A refresh token id must not be empty.", nameof(id));
        }

        if (administratorId == AdministratorId.Empty)
        {
            throw new ArgumentException("An administrator id must not be empty.", nameof(administratorId));
        }

        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Lifetime must be positive.");
        }

        var rawValue = GenerateRawValue();
        var token = new RefreshToken(id, administratorId, HashRawValue(rawValue), now, now + lifetime);

        return new IssuedRefreshToken(token, rawValue);
    }

    /// <summary>
    /// Hashes a raw refresh-token value the same way <see cref="IssueNew"/> does, so a
    /// caller presenting a raw value back (refresh, logout) can look it up by
    /// <see cref="TokenHash"/> without ever storing or comparing the raw value itself.
    /// </summary>
    public static string HashRawValue(string rawValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawValue);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawValue)));
    }

    private static string GenerateRawValue()
    {
        // 256 bits of randomness, base64url-encoded (no padding) so the value is safe
        // to place in a JSON string or a URL without further escaping.
        var bytes = RandomNumberGenerator.GetBytes(32);

        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Revokes the token. Strict, like <c>District.Activate</c>/<c>Deactivate</c> (Stage
    /// 1): revoking an already-revoked token is a domain error. Callers that need to
    /// revoke a set of tokens idempotently (reuse detection revoking every active session)
    /// filter to <see cref="IsActive"/> first, at the Application layer, the same pattern
    /// already used for ActivateDistrict/DeactivateDistrict (Stage 5).
    /// </summary>
    public Result Revoke(DateTimeOffset now, RefreshTokenId? replacedBy = null)
    {
        if (RevokedAt is not null)
        {
            return Result.Failure(AdministratorErrors.InvalidRefreshToken);
        }

        RevokedAt = now;
        ReplacedByTokenId = replacedBy;

        return Result.Success();
    }
}
