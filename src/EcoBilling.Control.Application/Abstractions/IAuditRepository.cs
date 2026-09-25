namespace EcoBilling.Control.Application.Abstractions;

/// <summary>
/// Read access to the audit trail, for ListAuditEntries. Deliberately separate from
/// <see cref="IAuditWriter"/>: six different handlers depend on writing an audit entry,
/// and none of them need read access to satisfy that -- only the one scenario that
/// displays the trail does.
/// </summary>
public interface IAuditRepository
{
    Task<PagedResult<AuditEntry>> ListAsync(int skip, int take, CancellationToken cancellationToken);
}
