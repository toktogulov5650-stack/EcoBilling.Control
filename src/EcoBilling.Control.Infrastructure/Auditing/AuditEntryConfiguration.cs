using EcoBilling.Control.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoBilling.Control.Infrastructure.Auditing;

/// <summary>Maps <see cref="AuditEntry"/> onto the <c>AuditEntries</c> table.</summary>
/// <remarks>
/// <see cref="AuditEntry"/> is an Application-level record, not a Domain entity -- it has
/// no business invariants of its own. It is mapped directly here, the same way
/// District/Administrator/RefreshToken are, rather than through a separate
/// Infrastructure-only persistence DTO, for consistency with the rest of this codebase.
/// <c>AdministratorId</c> deliberately has no foreign key (see <see cref="AuditEntry"/>'s
/// own remarks for why).
/// </remarks>
public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.AdministratorId);

        builder.Property(e => e.Action)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.EntityType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.EntityId)
            .HasMaxLength(100);

        builder.Property(e => e.BeforeData).HasColumnType("jsonb");
        builder.Property(e => e.AfterData).HasColumnType("jsonb");

        builder.Property(e => e.CorrelationId).HasMaxLength(100);
        builder.Property(e => e.IpAddress).HasMaxLength(64);
        builder.Property(e => e.UserAgent).HasMaxLength(512);

        builder.Property(e => e.CreatedAt).IsRequired();

        // Indexes specified in the architecture doc (section 18.1, Stage 1) for
        // investigation queries: "everything about entity X", "everything since date Y".
        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => new { e.EntityType, e.EntityId });
        builder.HasIndex(e => e.AdministratorId);
    }
}
