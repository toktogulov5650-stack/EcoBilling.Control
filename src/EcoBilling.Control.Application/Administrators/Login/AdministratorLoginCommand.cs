using EcoBilling.Control.Application.Abstractions;

namespace EcoBilling.Control.Application.Administrators.Login;

public sealed record AdministratorLoginCommand(string? Email, string? Password) : ICommand<AdministratorLoginResult>;
