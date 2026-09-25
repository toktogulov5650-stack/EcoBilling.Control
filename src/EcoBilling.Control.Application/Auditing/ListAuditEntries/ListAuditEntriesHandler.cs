using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.Application.Auditing.ListAuditEntries;

/// <summary>Reads a page of the audit trail. Never writes -- viewing is not itself an audited action.</summary>
public sealed class ListAuditEntriesHandler(IAuditRepository auditRepository)
    : IQueryHandler<ListAuditEntriesQuery, ListAuditEntriesResult>
{
    public async Task<Result<ListAuditEntriesResult>> HandleAsync(
        ListAuditEntriesQuery query,
        CancellationToken cancellationToken)
    {
        var (page, pageSize, skip) = Pagination.Normalize(query.Page, query.PageSize);

        var paged = await auditRepository.ListAsync(skip, pageSize, cancellationToken);

        return Result.Success(new ListAuditEntriesResult(paged.Items, paged.TotalCount, page, pageSize));
    }
}
