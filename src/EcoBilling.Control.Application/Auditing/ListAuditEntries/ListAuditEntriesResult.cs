using EcoBilling.Control.Application.Abstractions;

namespace EcoBilling.Control.Application.Auditing.ListAuditEntries;

public sealed record ListAuditEntriesResult(IReadOnlyList<AuditEntry> Items, int TotalCount, int Page, int PageSize);
