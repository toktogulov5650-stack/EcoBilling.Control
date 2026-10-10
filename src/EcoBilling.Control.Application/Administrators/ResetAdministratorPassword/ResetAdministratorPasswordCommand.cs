using EcoBilling.Control.Application.Abstractions;

namespace EcoBilling.Control.Application.Administrators.ResetAdministratorPassword;

/// <summary>Resets a system administrator password from the trusted provisioning CLI.</summary>
public sealed record ResetAdministratorPasswordCommand(string? Email, string? NewPassword)
    : ICommand<ResetAdministratorPasswordResult>;
