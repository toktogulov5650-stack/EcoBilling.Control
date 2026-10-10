using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Abstractions;

/// <summary>
/// Data required by a district to create its first director. The password is forwarded
/// only in memory over the authenticated service call; Control never persists or audits it.
/// </summary>
public sealed record CreateDirectorRequest(string FullName, string Email, string Password)
{
    public override string ToString() =>
        $"{nameof(CreateDirectorRequest)} {{ FullName = [REDACTED], Email = [REDACTED], Password = [REDACTED] }}";
}

/// <summary>The district's acknowledgement that a director now exists.</summary>
public sealed record DirectorCreationAcknowledged(string DirectorId);

/// <summary>
/// Calls a district's own protected internal API (architecture doc, section 20). This is
/// the only seam through which Control ever reaches a district's EcoBilling instance --
/// Application code never sees an HTTP status code or response body; every outcome is a
/// <see cref="Result"/> carrying one of the error codes in
/// <c>ProvisioningOperationErrors</c>.
/// </summary>
public interface IDistrictClient
{
    /// <summary>
    /// Asks the district to create its first director. <paramref name="idempotencyKey"/>
    /// is sent as the <c>Idempotency-Key</c> header; a retried call with the same key
    /// against a district that already processed it is expected to come back as a
    /// success carrying the original result, not a failure (architecture doc, section
    /// 20.2: <c>operation.already_processed</c> is accepted, not treated as an error).
    /// </summary>
    Task<Result<DirectorCreationAcknowledged>> CreateDirectorAsync(
        District district,
        CreateDirectorRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Asks the district to replace the named director's password with the supplied value.
    /// The password is forwarded only and must never be persisted or audited by Control.
    /// </summary>
    Task<Result> ResetDirectorPasswordAsync(
        District district,
        string directorEmail,
        string newPassword,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
