namespace EcoBilling.Control.Application.Abstractions;

/// <summary>
/// Records an administrative audit entry.
/// </summary>
/// <remarks>
/// Synchronous and void, exactly like <c>IDistrictRepository.Add</c>: this only *stages*
/// the entry into the same unit of work the calling handler is already using. The
/// handler's own existing <see cref="IUnitOfWork.SaveChangesAsync"/> call commits the
/// audit row and the business change it accompanies atomically, in one transaction --
/// there is no separate save here, and no way for one to persist without the other.
/// </remarks>
public interface IAuditWriter
{
    void Write(AuditEntry entry);
}
