using EcoBilling.Control.Domain.Administrators;

namespace EcoBilling.Control.Application.Administrators.ResetAdministratorPassword;

public sealed record ResetAdministratorPasswordResult(AdministratorId AdministratorId, string NormalizedEmail);
