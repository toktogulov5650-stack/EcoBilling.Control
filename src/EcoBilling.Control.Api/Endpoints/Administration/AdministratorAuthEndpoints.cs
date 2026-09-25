using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Administrators.Login;
using EcoBilling.Control.Application.Administrators.LogoutAdministratorSession;
using EcoBilling.Control.Application.Administrators.RefreshAdministratorSession;
using EcoBilling.Control.Domain.Administrators;

namespace EcoBilling.Control.Api.Endpoints.Administration;

/// <summary>Administrator session endpoints: login, refresh, logout.</summary>
public static class AdministratorAuthEndpoints
{
    public static IEndpointRouteBuilder MapAdministratorAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/auth");

        group.MapPost("/login", LoginAsync).WithName("AdministratorLogin");
        group.MapPost("/refresh", RefreshAsync).WithName("RefreshAdministratorSession");
        group.MapPost("/logout", LogoutAsync).WithName("LogoutAdministratorSession");

        return app;
    }

    // NOTE: ships with none of its three stated protections yet (doc, section 19.2:
    // "Rate limit, lockout, audit"). Rate limiting is Stage 14; lockout is Stage 1's
    // still-undecided open question; audit is Stage 7. Must not be exposed to
    // real/public traffic until Stage 14 and the lockout policy land -- the same
    // standing caveat already on ResolveDistrict (Stage 3).
    private static async Task<IResult> LoginAsync(
        AdministratorLoginRequest request,
        ICommandHandler<AdministratorLoginCommand, AdministratorLoginResult> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new AdministratorLoginCommand(request.Email, request.Password), cancellationToken);

        if (result.IsFailure)
        {
            return ApiError.ToResult(CollapseInactive(result.Error), httpContext);
        }

        var value = result.Value;

        return Results.Ok(new AdministratorSessionResponse(
            value.AccessToken,
            value.AccessTokenExpiresAt,
            value.RefreshToken,
            value.RefreshTokenExpiresAt));
    }

    private static async Task<IResult> RefreshAsync(
        RefreshAdministratorSessionRequest request,
        ICommandHandler<RefreshAdministratorSessionCommand, RefreshAdministratorSessionResult> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new RefreshAdministratorSessionCommand(request.RefreshToken), cancellationToken);

        if (result.IsFailure)
        {
            return ApiError.ToResult(result.Error, httpContext);
        }

        var value = result.Value;

        return Results.Ok(new AdministratorSessionResponse(
            value.AccessToken,
            value.AccessTokenExpiresAt,
            value.RefreshToken,
            value.RefreshTokenExpiresAt));
    }

    private static async Task<IResult> LogoutAsync(
        LogoutAdministratorSessionRequest request,
        ICommandHandler<LogoutAdministratorSessionCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        // Always succeeds (LogoutAdministratorSessionHandler, Stage 6) -- idempotent by
        // design, so there is no failure branch to map here.
        await handler.HandleAsync(new LogoutAdministratorSessionCommand(request.RefreshToken), cancellationToken);

        return Results.NoContent();
    }

    /// <summary>
    /// Collapses <c>administrator.inactive</c> onto the exact same response as
    /// <c>administrator.invalid_credentials</c> -- the two must be indistinguishable to
    /// the caller (doc, section 21.2), even though the handler tracks them separately
    /// internally for a future audit trail (Stage 7).
    /// </summary>
    private static Domain.Common.Error CollapseInactive(Domain.Common.Error error) =>
        error.Code == AdministratorErrors.Inactive.Code
            ? AdministratorErrors.InvalidCredentials
            : error;
}

public sealed record AdministratorLoginRequest(string? Email, string? Password);

public sealed record RefreshAdministratorSessionRequest(string? RefreshToken);

public sealed record LogoutAdministratorSessionRequest(string? RefreshToken);

public sealed record AdministratorSessionResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
