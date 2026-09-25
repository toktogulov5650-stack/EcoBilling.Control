using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Districts.ResolveDistrict;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Api.Endpoints.Public;

/// <summary>Public, unauthenticated endpoint that resolves a district code to its trusted API address.</summary>
public static class ResolveDistrictEndpoint
{
    /// <summary>
    /// Overrides <see cref="DistrictErrors.Inactive"/>'s message for this endpoint only, with a
    /// generic, actionable string a district's own residents can act on. The domain error itself
    /// stays untouched -- it is also returned by CreateDirector/ResetDirectorPassword (architecture
    /// doc, section 19.2), whose caller is the system administrator who deactivated the district,
    /// not one of its residents, so "contact your district's administrator" would not make sense
    /// there. The code (and therefore the HTTP status, via <see cref="ApiError"/>) is unchanged.
    /// </summary>
    private static readonly Error InactiveWithGuidance = new(
        DistrictErrors.Inactive.Code,
        "Обратитесь к администратору вашего округа.");

    public static IEndpointRouteBuilder MapResolveDistrict(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/districts/resolve", HandleAsync)
            .RequireRateLimiting("resolve-district")
            .WithName("ResolveDistrict");

        return app;
    }

    private static async Task<IResult> HandleAsync(
        ResolveDistrictRequest request,
        IQueryHandler<ResolveDistrictQuery, ResolveDistrictResult> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ResolveDistrictQuery(request.DistrictCode), cancellationToken);

        if (result.IsFailure)
        {
            var error = result.Error.Code == DistrictErrors.Inactive.Code ? InactiveWithGuidance : result.Error;

            return ApiError.ToResult(error, httpContext);
        }

        var value = result.Value;

        return Results.Ok(new ResolveDistrictResponse(
            value.DistrictCode,
            WithoutRedundantRootSlash(value.ApiBaseUrl),
            value.ExpiresInSeconds));
    }

    // Bare-host addresses (no path) canonicalize with a trailing "/" -- .NET's Uri class
    // always includes a root path, and this is correct, already-tested behavior in
    // TrustedApiUrl (Stage 1) that must not change. Stripped only here, at the wire
    // boundary: a client that naively concatenates its own path onto this value
    // ("apiBaseUrl + \"/internal/v1/...\"") would otherwise end up with "host//path".
    private static string WithoutRedundantRootSlash(string apiBaseUrl) =>
        Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var uri) && uri.AbsolutePath == "/"
            ? apiBaseUrl[..^1]
            : apiBaseUrl;
}

/// <summary>Request body for <see cref="ResolveDistrictEndpoint"/>.</summary>
public sealed record ResolveDistrictRequest(string? DistrictCode);

/// <summary>Response body for a successful resolve.</summary>
public sealed record ResolveDistrictResponse(string DistrictCode, string ApiBaseUrl, int ExpiresInSeconds);
