using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Provisioning.CreateDirector;

/// <summary>
/// Asks a district to create its first director with an administrator-selected password.
/// The password is forwarded to the district and is never persisted or audited by Control.
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
    string? Password,
    AdministratorId CallingAdministratorId) : ICommand<CreateDirectorResult>
{
    public override string ToString() =>
        $"{nameof(CreateDirectorCommand)} {{ DistrictId = {DistrictId}, FullName = [REDACTED], Email = [REDACTED], Password = [REDACTED], CallingAdministratorId = {CallingAdministratorId} }}";
}
