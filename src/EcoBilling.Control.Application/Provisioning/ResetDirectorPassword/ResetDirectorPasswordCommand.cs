using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Provisioning.ResetDirectorPassword;

/// <summary>
/// Asks a district to reset a director's password. Identifies the director by email --
/// Control does not store director records (architecture doc, section 1.3), so it has no
/// director id of its own to pass. Carries no credential (Stage 8, section 9.4/9.5):
/// EcoBilling generates and delivers the new password itself.
/// </summary>
public sealed record ResetDirectorPasswordCommand(
    DistrictId DistrictId,
    string? DirectorEmail,
    AdministratorId CallingAdministratorId) : ICommand<ResetDirectorPasswordResult>;
