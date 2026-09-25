using EcoBilling.Control.Domain.Common;

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

    /// <summary>
    /// Commits like <see cref="SaveChangesAsync"/>, but reports a unique-constraint
    /// violation as a failed <see cref="Result"/> (<see cref="UnitOfWorkErrors.ConcurrencyConflict"/>)
    /// instead of letting the underlying storage exception propagate. Narrowly scoped
    /// (Stage 9) to callers with a specific, anticipated race to handle -- currently only
    /// <c>CreateDirectorHandler</c>. <see cref="SaveChangesAsync"/> itself is unaffected
    /// and keeps throwing on any storage failure exactly as before this method existed.
    /// </summary>
    Task<Result> SaveChangesOrConflictAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Generic, storage-level error codes <see cref="IUnitOfWork"/> itself can produce --
/// not tied to any specific feature's business meaning. A caller translates
/// <see cref="ConcurrencyConflict"/> into its own domain-specific error code
/// (e.g. CreateDirectorHandler maps it to <c>provisioning.concurrent_conflict</c>).
/// </summary>
public static class UnitOfWorkErrors
{
    public static readonly Error ConcurrencyConflict = new(
        "persistence.concurrency_conflict",
        "A concurrent write conflicted with a uniqueness constraint.");
}
