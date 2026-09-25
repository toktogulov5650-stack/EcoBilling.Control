using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Provisioning.CreateDirector;
using EcoBilling.Control.Application.Provisioning.GetProvisioningOperation;
using EcoBilling.Control.Application.Provisioning.ResetDirectorPassword;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Domain.Provisioning;

namespace EcoBilling.Control.Api.Endpoints.Administration;

/// <summary>
/// The provisioning endpoints Stage 8 built handlers for: CreateDirector and
/// ResetDirectorPassword call a district's protected internal API (architecture doc,
/// section 20), and GetProvisioningOperation reports the resulting operation's status.
/// </summary>
public static class ProvisioningEndpoints
{
    public static IEndpointRouteBuilder MapProvisioningEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/admin/districts/{districtId:guid}/directors", CreateDirectorAsync)
            .RequireAuthorization("SystemAdmin")
            .WithName("CreateDirector");

        app.MapPost("/api/v1/admin/districts/{districtId:guid}/directors/reset-password", ResetDirectorPasswordAsync)
            .RequireAuthorization("SystemAdmin")
            .WithName("ResetDirectorPassword");

        app.MapGet("/api/v1/admin/operations/{operationId:guid}", GetOperationAsync)
            .RequireAuthorization("SystemAdmin")
            .WithName("GetProvisioningOperation");

        return app;
    }

    private static async Task<IResult> CreateDirectorAsync(
        Guid districtId,
        CreateDirectorHttpRequest request,
        ICommandHandler<CreateDirectorCommand, CreateDirectorResult> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var command = new CreateDirectorCommand(
            new DistrictId(districtId), request.FullName, request.Email, CallingAdministrator.Resolve(httpContext));

        var result = await handler.HandleAsync(command, cancellationToken);

        if (result.IsFailure)
        {
            return ApiError.ToResult(result.Error, httpContext);
        }

        return Results.Created(
            $"/api/v1/admin/operations/{result.Value.OperationId.Value}",
            new CreateDirectorHttpResponse(result.Value.OperationId.Value.ToString(), result.Value.DirectorId));
    }

    private static async Task<IResult> ResetDirectorPasswordAsync(
        Guid districtId,
        ResetDirectorPasswordHttpRequest request,
        ICommandHandler<ResetDirectorPasswordCommand, ResetDirectorPasswordResult> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var command = new ResetDirectorPasswordCommand(
            new DistrictId(districtId), request.DirectorEmail, CallingAdministrator.Resolve(httpContext));

        var result = await handler.HandleAsync(command, cancellationToken);

        if (result.IsFailure)
        {
            return ApiError.ToResult(result.Error, httpContext);
        }

        return Results.Accepted(
            $"/api/v1/admin/operations/{result.Value.OperationId.Value}",
            new ResetDirectorPasswordHttpResponse(result.Value.OperationId.Value.ToString()));
    }

    private static async Task<IResult> GetOperationAsync(
        Guid operationId,
        IQueryHandler<GetProvisioningOperationQuery, GetProvisioningOperationResult> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new GetProvisioningOperationQuery(new ProvisioningOperationId(operationId)), cancellationToken);

        if (result.IsFailure)
        {
            return ApiError.ToResult(result.Error, httpContext);
        }

        var value = result.Value;

        return Results.Ok(new ProvisioningOperationResponse(
            value.OperationId.Value.ToString(),
            value.DistrictId.Value.ToString(),
            value.OperationType.ToString(),
            value.Status.ToString(),
            value.AttemptCount,
            value.CreatedAt,
            value.StartedAt,
            value.CompletedAt,
            value.FailedAt,
            value.LastErrorCode));
    }
}

public sealed record CreateDirectorHttpRequest(string? FullName, string? Email);

public sealed record CreateDirectorHttpResponse(string OperationId, string DirectorId);

public sealed record ResetDirectorPasswordHttpRequest(string? DirectorEmail);

public sealed record ResetDirectorPasswordHttpResponse(string OperationId);

public sealed record ProvisioningOperationResponse(
    string OperationId,
    string DistrictId,
    string OperationType,
    string Status,
    int AttemptCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FailedAt,
    string? LastErrorCode);
