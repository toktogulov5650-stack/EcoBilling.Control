using EcoBilling.Control.Domain.Administrators;

namespace EcoBilling.Control.Api.Endpoints.Administration;

/// <summary>
/// Extracts the authenticated caller's id from the validated access token's claims, for
/// endpoints that need to attribute an audit entry (Stage 7) to the administrator making
/// the request.
/// </summary>
/// <remarks>
/// Every caller of this is behind <c>RequireAuthorization("SystemAdmin")</c>, so a
/// missing or unparsable "sub" claim here means the auth pipeline itself is broken, not
/// that the client sent a bad request -- hence throwing rather than returning a client
/// error.
/// </remarks>
internal static class CallingAdministrator
{
    public static AdministratorId Resolve(HttpContext httpContext)
    {
        var sub = httpContext.User.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Expected an authenticated request to carry a 'sub' claim.");

        return new AdministratorId(Guid.Parse(sub));
    }
}
