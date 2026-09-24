using EcoBilling.Control.Application.Abstractions;

namespace EcoBilling.Control.Infrastructure.Persistence;

/// <summary>Commits a scenario's changes via <see cref="ControlDbContext.SaveChangesAsync"/>.</summary>
/// <remarks>Has no caller yet -- CreateDistrict (Stage 5) is its first consumer.</remarks>
public sealed class EfUnitOfWork(ControlDbContext dbContext) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
