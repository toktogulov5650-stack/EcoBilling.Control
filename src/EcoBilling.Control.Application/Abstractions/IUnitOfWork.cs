namespace EcoBilling.Control.Application.Abstractions;

/// <summary>
/// Commits the changes a scenario made through its repositories as a single unit.
/// </summary>
/// <remarks>
/// Introduced in this stage together with the other abstractions so every future
/// write-scenario handler can depend on it from the start. It has no consumer yet:
/// ResolveDistrict (Stage 3) is read-only, and CreateDistrict (Stage 5) is its first
/// real caller.
/// </remarks>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
