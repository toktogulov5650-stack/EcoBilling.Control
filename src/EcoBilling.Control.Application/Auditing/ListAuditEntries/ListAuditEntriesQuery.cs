using EcoBilling.Control.Application.Abstractions;

namespace EcoBilling.Control.Application.Auditing.ListAuditEntries;

public sealed record ListAuditEntriesQuery(int Page, int PageSize) : IQuery<ListAuditEntriesResult>;
