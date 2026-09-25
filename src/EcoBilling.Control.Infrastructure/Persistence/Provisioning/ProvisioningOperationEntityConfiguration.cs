using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Domain.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoBilling.Control.Infrastructure.Persistence.Provisioning;

/// <summary>Maps <see cref="ProvisioningOperation"/> onto the <c>ProvisioningOperations</c> table.</summary>
public sealed class ProvisioningOperationEntityConfiguration : IEntityTypeConfiguration<ProvisioningOperation>
{
    private const int IdempotencyKeyMaxLength = 64; // Guid.ToString() is 36 chars; headroom for a future format.
    private const int ErrorCodeMaxLength = 100; // matches the Error.Code convention used elsewhere (e.g. AuditEntry.Action).

    public void Configure(EntityTypeBuilder<ProvisioningOperation> builder)
    {
        builder.ToTable("ProvisioningOperations");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasConversion(id => id.Value, value => new ProvisioningOperationId(value))
            .ValueGeneratedNever();

        builder.Property(o => o.DistrictId)
            .HasConversion(id => id.Value, value => new DistrictId(value))
            .IsRequired();

        // Property-only foreign key (no navigation on either side, same pattern as
        // RefreshToken -> Administrator, Stage 6) -- still a real FK constraint at the
        // database level.
        builder.HasOne<District>()
            .WithMany()
            .HasForeignKey(o => o.DistrictId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(o => o.OperationType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(o => o.IdempotencyKey)
            .HasMaxLength(IdempotencyKeyMaxLength)
            .IsRequired();

        // Idempotency is enforced by this constraint, not only by in-memory logic
        // (architecture doc, section 18.1) -- same reasoning as
        // Districts(NormalizedCode).
        builder.HasIndex(o => o.IdempotencyKey)
            .IsUnique();

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(o => o.AttemptCount).IsRequired();
        builder.Property(o => o.CreatedAt).IsRequired();
        builder.Property(o => o.StartedAt);
        builder.Property(o => o.CompletedAt);
        builder.Property(o => o.FailedAt);
        builder.Property(o => o.LastErrorCode).HasMaxLength(ErrorCodeMaxLength);

        // For retry/monitoring of stuck operations (architecture doc, section 18.1).
        builder.HasIndex(o => new { o.Status, o.CreatedAt });

        // GetLatestAsync's query shape (Stage 8, section 9.2's idempotency-key reuse).
        builder.HasIndex(o => new { o.DistrictId, o.OperationType });
    }
}
