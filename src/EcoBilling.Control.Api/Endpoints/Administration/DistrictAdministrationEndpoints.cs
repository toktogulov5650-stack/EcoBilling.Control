using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Districts.ActivateDistrict;
using EcoBilling.Control.Application.Districts.CreateDistrict;
using EcoBilling.Control.Application.Districts.DeactivateDistrict;
using EcoBilling.Control.Application.Districts.GetDistrict;
using EcoBilling.Control.Application.Districts.ListDistricts;
using EcoBilling.Control.Application.Districts.UpdateDistrict;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Api.Endpoints.Administration;

/// <summary>
/// The district admin endpoints Stage 5 built handlers for but never mapped to HTTP --
/// deferred until both SystemAdmin auth (Stage 6) and auditing (Stage 7) existed, so
/// they go live authenticated and audited from day one.
/// </summary>
public static class DistrictAdministrationEndpoints
{
    public static IEndpointRouteBuilder MapDistrictAdministrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/districts").RequireAuthorization("SystemAdmin");

        group.MapPost("/", CreateAsync).WithName("CreateDistrict");
        group.MapGet("/", ListAsync).WithName("ListDistricts");
        group.MapGet("/{districtId:guid}", GetAsync).WithName("GetDistrict");
        group.MapPut("/{districtId:guid}", UpdateAsync).WithName("UpdateDistrict");
        group.MapPost("/{districtId:guid}/activate", ActivateAsync).WithName("ActivateDistrict");
        group.MapPost("/{districtId:guid}/deactivate", DeactivateAsync).WithName("DeactivateDistrict");

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateDistrictRequest request,
        ICommandHandler<CreateDistrictCommand, CreateDistrictResult> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var command = new CreateDistrictCommand(
            request.Code, request.Name, request.ApiBaseUrl, CallingAdministrator.Resolve(httpContext));

        var result = await handler.HandleAsync(command, cancellationToken);

        if (result.IsFailure)
        {
            return ApiError.ToResult(result.Error, httpContext);
        }

        return Results.Created(
            $"/api/v1/admin/districts/{result.Value.DistrictId.Value}",
            new CreateDistrictResponse(result.Value.DistrictId.Value.ToString(), result.Value.NormalizedCode));
    }

    private static async Task<IResult> ListAsync(
        int? page,
        int? pageSize,
        IQueryHandler<ListDistrictsQuery, ListDistrictsResult> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListDistrictsQuery(page ?? 1, pageSize ?? 0), cancellationToken);

        if (result.IsFailure)
        {
            return ApiError.ToResult(result.Error, httpContext);
        }

        var value = result.Value;

        return Results.Ok(new ListDistrictsResponse(
            value.Items.Select(ToResponse).ToList(),
            value.TotalCount,
            value.Page,
            value.PageSize));
    }

    private static async Task<IResult> GetAsync(
        Guid districtId,
        IQueryHandler<GetDistrictQuery, GetDistrictResult> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetDistrictQuery(new DistrictId(districtId)), cancellationToken);

        if (result.IsFailure)
        {
            return ApiError.ToResult(result.Error, httpContext);
        }

        return Results.Ok(ToResponse(result.Value));
    }

    private static async Task<IResult> UpdateAsync(
        Guid districtId,
        UpdateDistrictRequest request,
        ICommandHandler<UpdateDistrictCommand, Unit> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var command = new UpdateDistrictCommand(
            new DistrictId(districtId), request.Name, request.ApiBaseUrl, CallingAdministrator.Resolve(httpContext));

        var result = await handler.HandleAsync(command, cancellationToken);

        return result.IsFailure ? ApiError.ToResult(result.Error, httpContext) : Results.NoContent();
    }

    private static async Task<IResult> ActivateAsync(
        Guid districtId,
        ICommandHandler<ActivateDistrictCommand, Unit> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var command = new ActivateDistrictCommand(new DistrictId(districtId), CallingAdministrator.Resolve(httpContext));
        var result = await handler.HandleAsync(command, cancellationToken);

        return result.IsFailure ? ApiError.ToResult(result.Error, httpContext) : Results.NoContent();
    }

    private static async Task<IResult> DeactivateAsync(
        Guid districtId,
        ICommandHandler<DeactivateDistrictCommand, Unit> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var command = new DeactivateDistrictCommand(new DistrictId(districtId), CallingAdministrator.Resolve(httpContext));
        var result = await handler.HandleAsync(command, cancellationToken);

        return result.IsFailure ? ApiError.ToResult(result.Error, httpContext) : Results.NoContent();
    }

    private static DistrictResponse ToResponse(GetDistrictResult district) =>
        new(
            district.DistrictId.Value.ToString(),
            district.Code,
            district.Name,
            district.ApiBaseUrl,
            district.Status.ToString(),
            district.CreatedAt,
            district.UpdatedAt,
            district.ActivatedAt,
            district.DeactivatedAt);

    private static DistrictResponse ToResponse(DistrictSummary district) =>
        new(
            district.DistrictId.Value.ToString(),
            district.Code,
            district.Name,
            district.ApiBaseUrl,
            district.Status.ToString(),
            district.CreatedAt,
            district.UpdatedAt,
            district.ActivatedAt,
            district.DeactivatedAt);
}

public sealed record CreateDistrictRequest(string? Code, string? Name, string? ApiBaseUrl);

public sealed record CreateDistrictResponse(string DistrictId, string NormalizedCode);

public sealed record UpdateDistrictRequest(string? Name, string? ApiBaseUrl);

public sealed record DistrictResponse(
    string DistrictId,
    string Code,
    string Name,
    string ApiBaseUrl,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? DeactivatedAt);

public sealed record ListDistrictsResponse(IReadOnlyList<DistrictResponse> Items, int TotalCount, int Page, int PageSize);
