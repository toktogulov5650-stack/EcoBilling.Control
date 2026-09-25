namespace EcoBilling.Control.Application.Administrators.Login;

public sealed record AdministratorLoginResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
