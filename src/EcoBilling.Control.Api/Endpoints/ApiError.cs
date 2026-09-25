using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.Api.Endpoints;

/// <summary>
/// Maps a domain <see cref="Error"/> to the client-facing error envelope: code, message
/// and traceId (architecture doc, section 20).
/// </summary>
/// <remarks>
/// Scoped to the error codes ResolveDistrict can actually produce. As more scenarios
/// reach the Api layer with their own error codes, either this table grows or it gets
/// promoted to shared Problem Details middleware -- whichever stops duplication first.
/// An error code this table does not recognize maps to 500: an error the Api layer
/// cannot classify is a server-side bug, not something to blame on the client.
/// </remarks>
public static class ApiError
{
    private static readonly Dictionary<string, int> StatusCodesByErrorCode = new(StringComparer.Ordinal)
    {
        ["district.invalid_code"] = StatusCodes.Status400BadRequest,
        ["district.invalid_url"] = StatusCodes.Status400BadRequest,
        ["district.invalid_name"] = StatusCodes.Status400BadRequest,
        ["district.host_not_allowed"] = StatusCodes.Status400BadRequest,
        ["validation.failed"] = StatusCodes.Status400BadRequest,
        ["district.not_found"] = StatusCodes.Status404NotFound,
        ["district.inactive"] = StatusCodes.Status404NotFound,
        ["district.code_conflict"] = StatusCodes.Status409Conflict,
        ["district.invalid_status_transition"] = StatusCodes.Status409Conflict,
        ["administrator.invalid_credentials"] = StatusCodes.Status401Unauthorized,
        ["administrator.invalid_refresh_token"] = StatusCodes.Status401Unauthorized,

        // Stage 8: CreateDirector / ResetDirectorPassword / GetProvisioningOperation.
        ["provisioning.operation_not_found"] = StatusCodes.Status404NotFound,
        ["director.not_found"] = StatusCodes.Status404NotFound,
        ["director.already_exists"] = StatusCodes.Status409Conflict,
        ["district.unavailable"] = StatusCodes.Status503ServiceUnavailable,
        ["service.unauthorized"] = StatusCodes.Status502BadGateway,

        // Stage 9: two concurrent CreateDirector requests for the same district.
        ["provisioning.concurrent_conflict"] = StatusCodes.Status409Conflict,
    };

    public static IResult ToResult(Error error, HttpContext httpContext)
    {
        var statusCode = StatusCodesByErrorCode.GetValueOrDefault(error.Code, StatusCodes.Status500InternalServerError);

        return Results.Json(
            new ApiErrorResponse(error.Code, error.Message, httpContext.TraceIdentifier),
            statusCode: statusCode);
    }
}

/// <summary>The error envelope every Control endpoint returns on failure.</summary>
public sealed record ApiErrorResponse(string Code, string Message, string TraceId);
