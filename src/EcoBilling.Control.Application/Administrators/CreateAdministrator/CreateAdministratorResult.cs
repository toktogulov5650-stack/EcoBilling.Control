using EcoBilling.Control.Domain.Administrators;

namespace EcoBilling.Control.Application.Administrators.CreateAdministrator;

public sealed record CreateAdministratorResult(AdministratorId AdministratorId, string NormalizedEmail);
