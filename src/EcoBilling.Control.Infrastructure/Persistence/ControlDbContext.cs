using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Infrastructure.Persistence.Districts;
using Microsoft.EntityFrameworkCore;

namespace EcoBilling.Control.Infrastructure.Persistence;

/// <summary>The central registry's database context. Owns no data belonging to any district.</summary>
public sealed class ControlDbContext(DbContextOptions<ControlDbContext> options) : DbContext(options)
{
    public DbSet<District> Districts => Set<District>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new DistrictEntityConfiguration());
    }
}
