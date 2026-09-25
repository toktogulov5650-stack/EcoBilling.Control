namespace EcoBilling.Control.Application.Administrators.RefreshAdministratorSession;

public sealed record RefreshAdministratorSessionResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
