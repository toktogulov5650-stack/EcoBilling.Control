using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Provisioning.CreateDirector;

/// <summary>
/// Asks a district to create its first director. Carries no credential (Stage 8, section
/// 9.4): EcoBilling generates and delivers the director's initial password itself.
/// </summary>
/// <param name="CallingAdministratorId">
/// The authenticated administrator making this request, purely so this handler can
/// attribute the audit entry to the right actor -- not a business parameter of "create a
/// director" itself (same pattern as CreateDistrictCommand, Stage 7).
/// </param>
public sealed record CreateDirectorCommand(
    DistrictId DistrictId,
    string? FullName,
    string? Email,
    AdministratorId CallingAdministratorId) : ICommand<CreateDirectorResult>;
