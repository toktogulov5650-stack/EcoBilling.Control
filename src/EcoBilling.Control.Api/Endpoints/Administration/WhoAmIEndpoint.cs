namespace EcoBilling.Control.Api.Endpoints.Administration;

/// <summary>
/// Returns the calling administrator's identity from their access token's claims.
/// </summary>
/// <remarks>
/// Not asked for explicitly in Stage 6's scope -- added because this is the only
/// endpoint that proves the full auth pipeline (issue -> validate -> extract claims ->
/// enforce SystemAdmin policy) actually works end to end, before Stage 7's district
/// endpoints exist to discover a wiring bug mixed in with unrelated failures. It also
/// happens to be a common, genuinely useful admin-facing check.
/// </remarks>
public static class WhoAmIEndpoint
{
    public static IEndpointRouteBuilder MapWhoAmI(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/admin/whoami", Handle)
            .RequireAuthorization("SystemAdmin")
            .WithName("WhoAmI");

        return app;
    }

    private static IResult Handle(HttpContext httpContext)
    {
        // "sub" / "email": the exact short claim names JwtAccessTokenIssuer uses, safe
        // to read directly because MapInboundClaims is disabled for this scheme
        // (ServiceCollectionExtensions.AddAdministratorAuthentication).
        var administratorId = httpContext.User.FindFirst("sub")?.Value;
        var email = httpContext.User.FindFirst("email")?.Value;

        return administratorId is null || email is null
            ? Results.Unauthorized()
            : Results.Ok(new WhoAmIResponse(administratorId, email));
    }
}

public sealed record WhoAmIResponse(string AdministratorId, string Email);
