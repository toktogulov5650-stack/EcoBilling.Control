using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Provisioning.ResetDirectorPassword;

/// <summary>
/// Asks a district to reset a director's password. Identifies the director by email --
/// Control does not store director records (architecture doc, section 1.3), so it has no
/// director id of its own to pass. The new password is forwarded to the district and is
/// never persisted or audited by Control.
/// </summary>
public sealed record ResetDirectorPasswordCommand(
    DistrictId DistrictId,
    string? Email,
    string? NewPassword,
    AdministratorId CallingAdministratorId) : ICommand<ResetDirectorPasswordResult>
{
    public override string ToString() =>
        $"{nameof(ResetDirectorPasswordCommand)} {{ DistrictId = {DistrictId}, Email = [REDACTED], NewPassword = [REDACTED], CallingAdministratorId = {CallingAdministratorId} }}";
}
