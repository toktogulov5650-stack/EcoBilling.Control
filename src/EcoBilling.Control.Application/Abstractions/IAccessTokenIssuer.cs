using EcoBilling.Control.Domain.Administrators;

namespace EcoBilling.Control.Application.Abstractions;

/// <summary>The signed access token issued to a logged-in administrator.</summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>Issues short-lived signed access tokens (JWTs) for administrators.</summary>
public interface IAccessTokenIssuer
{
    AccessToken Issue(Administrator administrator, DateTimeOffset now);
}
