using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EcoBilling.Control.Infrastructure.Persistence;

/// <summary>Commits a scenario's changes via <see cref="ControlDbContext.SaveChangesAsync"/>.</summary>
public sealed class EfUnitOfWork(ControlDbContext dbContext) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public async Task<Result> SaveChangesOrConflictAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Result.Failure(UnitOfWorkErrors.ConcurrencyConflict);
        }
    }
}
