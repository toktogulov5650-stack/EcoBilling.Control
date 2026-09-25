using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Auditing.ListAuditEntries;

namespace EcoBilling.Control.Api.Endpoints.Administration;

/// <summary>Read-only view of the administrative audit trail (Stage 7).</summary>
public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/admin/audit", ListAsync)
            .RequireAuthorization("SystemAdmin")
            .WithName("ListAuditEntries");

        return app;
    }

    private static async Task<IResult> ListAsync(
        int? page,
        int? pageSize,
        IQueryHandler<ListAuditEntriesQuery, ListAuditEntriesResult> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListAuditEntriesQuery(page ?? 1, pageSize ?? 0), cancellationToken);

        if (result.IsFailure)
        {
            return ApiError.ToResult(result.Error, httpContext);
        }

        var value = result.Value;

        return Results.Ok(new AuditEntriesResponse(
            value.Items.Select(ToResponse).ToList(),
            value.TotalCount,
            value.Page,
            value.PageSize));
    }

    private static AuditEntryResponse ToResponse(AuditEntry entry) =>
        new(
            entry.Id,
            entry.AdministratorId,
            entry.Action,
            entry.EntityType,
            entry.EntityId,
            entry.BeforeData,
            entry.AfterData,
            entry.CorrelationId,
            entry.IpAddress,
            entry.UserAgent,
            entry.CreatedAt);
}

public sealed record AuditEntryResponse(
    Guid Id,
    Guid? AdministratorId,
    string Action,
    string EntityType,
    string? EntityId,
    string? BeforeData,
    string? AfterData,
    string? CorrelationId,
    string? IpAddress,
    string? UserAgent,
    DateTimeOffset CreatedAt);

public sealed record AuditEntriesResponse(IReadOnlyList<AuditEntryResponse> Items, int TotalCount, int Page, int PageSize);
