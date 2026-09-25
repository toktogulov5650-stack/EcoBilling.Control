using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Districts.UpdateDistrict;

/// <summary>
/// Changes a district's name and/or API address. The district's code is never part of
/// this command -- Domain (Stage 1) exposes no mutator for it (brief, section 15: "once
/// a district is operating, its code should preferably not change"). Status changes go
/// through ActivateDistrict/DeactivateDistrict instead, since those are separate
/// business transitions with their own timestamps, not a field edit.
/// </summary>
/// <param name="CallingAdministratorId">
/// The authenticated administrator making this request, sourced by the endpoint from the
/// validated access token's "sub" claim -- purely so this handler can attribute the
/// audit entry (Stage 7) to the right actor.
/// </param>
public sealed record UpdateDistrictCommand(
    DistrictId DistrictId,
    string? Name,
    string? ApiBaseUrl,
    AdministratorId CallingAdministratorId) : ICommand<Unit>;
