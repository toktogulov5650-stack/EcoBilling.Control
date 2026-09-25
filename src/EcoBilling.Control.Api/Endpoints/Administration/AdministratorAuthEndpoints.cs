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

        group.MapPost("/login", LoginAsync).RequireRateLimiting("admin-login").WithName("AdministratorLogin");
        group.MapPost("/refresh", RefreshAsync).WithName("RefreshAdministratorSession");
        group.MapPost("/logout", LogoutAsync).WithName("LogoutAdministratorSession");

        return app;
    }

    private static async Task<IResult> LoginAsync(
        AdministratorLoginRequest request,
        ICommandHandler<AdministratorLoginCommand, AdministratorLoginResult> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new AdministratorLoginCommand(request.Email, request.Password), cancellationToken);

        if (result.IsFailure)
        {
            return ApiError.ToResult(CollapseToInvalidCredentials(result.Error), httpContext);
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
    /// Collapses <c>administrator.inactive</c> and <c>administrator.locked_out</c>
    /// (Stage 10) onto the exact same response as <c>administrator.invalid_credentials</c>
    /// -- all three must be indistinguishable to the caller (doc, section 21.2), even
    /// though the handler tracks them separately internally for the audit trail
    /// (Stage 7, Stage 10).
    /// </summary>
    private static Domain.Common.Error CollapseToInvalidCredentials(Domain.Common.Error error) =>
        error.Code == AdministratorErrors.Inactive.Code || error.Code == AdministratorErrors.LockedOut.Code
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
