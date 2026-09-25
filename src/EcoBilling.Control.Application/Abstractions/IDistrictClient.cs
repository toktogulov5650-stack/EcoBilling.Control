using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Abstractions;

/// <summary>
/// Requests Control's own CreateDirector command carries -- <c>fullName</c>/<c>email</c>
/// only. No credential field exists here at all (Stage 8, section 9.4): EcoBilling owns
/// generating the director's initial credential and communicating it to them; Control
/// never receives, holds, or transmits a director password in any form.
/// </summary>
public sealed record CreateDirectorRequest(string FullName, string Email);

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
    /// Asks the district to reset the named director's password. EcoBilling owns
    /// generating and delivering the new credential -- this call carries no password in
    /// either direction, only the identity of which director to reset (Stage 8, section
    /// 9.5, mirroring section 9.4's rule for CreateDirector).
    /// </summary>
    Task<Result> ResetDirectorPasswordAsync(
        District district,
        string directorEmail,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
