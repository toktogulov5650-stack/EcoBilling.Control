using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Districts.ActivateDistrict;

/// <param name="CallingAdministratorId">
/// The authenticated administrator making this request, sourced by the endpoint from the
/// validated access token's "sub" claim -- purely so this handler can attribute the
/// audit entry (Stage 7) to the right actor.
/// </param>
public sealed record ActivateDistrictCommand(DistrictId DistrictId, AdministratorId CallingAdministratorId) : ICommand<Unit>;
