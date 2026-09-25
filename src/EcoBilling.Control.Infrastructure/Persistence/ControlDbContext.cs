using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Domain.Provisioning;
using EcoBilling.Control.Infrastructure.Auditing;
using EcoBilling.Control.Infrastructure.Persistence.Administrators;
using EcoBilling.Control.Infrastructure.Persistence.Districts;
using EcoBilling.Control.Infrastructure.Persistence.Provisioning;
using Microsoft.EntityFrameworkCore;

namespace EcoBilling.Control.Infrastructure.Persistence;

/// <summary>The central registry's database context. Owns no data belonging to any district.</summary>
public sealed class ControlDbContext(DbContextOptions<ControlDbContext> options) : DbContext(options)
{
    public DbSet<District> Districts => Set<District>();

    public DbSet<Administrator> Administrators => Set<Administrator>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<ProvisioningOperation> ProvisioningOperations => Set<ProvisioningOperation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new DistrictEntityConfiguration());
        modelBuilder.ApplyConfiguration(new AdministratorEntityConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenEntityConfiguration());
        modelBuilder.ApplyConfiguration(new AuditEntryConfiguration());
        modelBuilder.ApplyConfiguration(new ProvisioningOperationEntityConfiguration());
    }
}
