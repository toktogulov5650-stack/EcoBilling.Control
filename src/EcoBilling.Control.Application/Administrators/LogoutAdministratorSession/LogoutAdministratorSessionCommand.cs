using EcoBilling.Control.Application.Abstractions;

namespace EcoBilling.Control.Application.Administrators.LogoutAdministratorSession;

public sealed record LogoutAdministratorSessionCommand(string? RefreshToken) : ICommand<Unit>;
