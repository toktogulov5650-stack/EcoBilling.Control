using EcoBilling.Control.Application.Abstractions;

namespace EcoBilling.Control.Application.Administrators.CreateAdministrator;

/// <summary>
/// Creates a system administrator. There is no HTTP endpoint for this -- unlike every
/// other scenario in this codebase, the only caller is the Provisioning console tool
/// (Q4): there is no public self-registration, and no admin-to-admin creation endpoint
/// either, so this is the sole path by which any administrator account -- the first one
/// or any later one -- ever comes to exist.
/// </summary>
public sealed record CreateAdministratorCommand(string? Email, string? FullName, string? Password)
    : ICommand<CreateAdministratorResult>;
