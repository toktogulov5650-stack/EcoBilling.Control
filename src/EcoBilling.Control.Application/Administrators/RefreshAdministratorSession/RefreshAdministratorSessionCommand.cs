using EcoBilling.Control.Application.Abstractions;

namespace EcoBilling.Control.Application.Administrators.RefreshAdministratorSession;

public sealed record RefreshAdministratorSessionCommand(string? RefreshToken)
    : ICommand<RefreshAdministratorSessionResult>;
