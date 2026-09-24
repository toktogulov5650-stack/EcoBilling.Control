using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Districts.ResolveDistrict;

namespace EcoBilling.Control.Api.Endpoints.Public;

/// <summary>Public, unauthenticated endpoint that resolves a district code to its trusted API address.</summary>
public static class ResolveDistrictEndpoint
{
    public static IEndpointRouteBuilder MapResolveDistrict(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/districts/resolve", HandleAsync)
            .WithName("ResolveDistrict");

        return app;
    }

    // NOTE: no rate limiting yet (Stage 14). Per the architecture doc (section 19.1) and
    // the brief (section 9), this endpoint must not be exposed to real traffic before
    // rate limiting is in place: an unlimited endpoint is a code-enumeration oracle by
    // response timing and volume alone, regardless of how safe the response body is.
    private static async Task<IResult> HandleAsync(
        ResolveDistrictRequest request,
        IQueryHandler<ResolveDistrictQuery, ResolveDistrictResult> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ResolveDistrictQuery(request.DistrictCode), cancellationToken);

        if (result.IsFailure)
        {
            return ApiError.ToResult(result.Error, httpContext);
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
