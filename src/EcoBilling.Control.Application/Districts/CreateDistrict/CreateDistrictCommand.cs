using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Administrators;

namespace EcoBilling.Control.Application.Districts.CreateDistrict;

/// <summary>Registers a new district in the central registry. Starts inactive (Domain, Stage 1).</summary>
/// <param name="CallingAdministratorId">
/// The authenticated administrator making this request, sourced by the endpoint from the
/// validated access token's "sub" claim -- purely so this handler can attribute the
/// audit entry (Stage 7) to the right actor. Not a business parameter of "create a
/// district" itself.
/// </param>
public sealed record CreateDistrictCommand(
    string? Code,
    string? Name,
    string? ApiBaseUrl,
    AdministratorId CallingAdministratorId) : ICommand<CreateDistrictResult>;
