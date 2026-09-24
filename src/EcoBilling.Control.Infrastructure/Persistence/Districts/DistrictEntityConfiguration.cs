using EcoBilling.Control.Domain.Districts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoBilling.Control.Infrastructure.Persistence.Districts;

/// <summary>Maps <see cref="District"/> onto the <c>Districts</c> table.</summary>
/// <remarks>
/// <see cref="District"/> exposes a private parameterless constructor purely for EF Core
/// to materialize rows through; every property here, including the get-only ones, is
/// written directly to its backing field by EF Core during materialization, bypassing
/// property accessors entirely. Nothing here required changing <see cref="District"/>'s
/// public surface or its Stage 1 invariants.
/// </remarks>
public sealed class DistrictEntityConfiguration : IEntityTypeConfiguration<District>
{
    // The regex behind DistrictCode (Stage 1, section 17.3 of the architecture doc) caps
    // a code at 10 letters + 1 hyphen + 4 digits = 15 characters. This leaves headroom
    // for the format to grow slightly without an immediate migration, while staying far
    // below DistrictCode.MaxInputLength (the raw-input guard, not the stored length).
    private const int CodeMaxLength = 32;

    public void Configure(EntityTypeBuilder<District> builder)
    {
        builder.ToTable("Districts");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasConversion(id => id.Value, value => new DistrictId(value))
            .ValueGeneratedNever();

        builder.Property(d => d.Code)
            .HasMaxLength(CodeMaxLength)
            .IsRequired();

        builder.Property(d => d.NormalizedCode)
            .HasMaxLength(CodeMaxLength)
            .IsRequired();

        builder.HasIndex(d => d.NormalizedCode)
            .IsUnique();

        builder.Property(d => d.Name)
            .HasMaxLength(District.MaxNameLength)
            .IsRequired();

        // Rehydration reuses the same validating factory as any other input (Stage 1).
        // A row that fails re-validation means the data was corrupted or edited outside
        // the application, which should fail loudly here rather than load silently.
        builder.Property(d => d.ApiBaseUrl)
            .HasConversion(
                url => url.ToString(),
                value => TrustedApiUrl.Create(value).Value)
            .HasMaxLength(TrustedApiUrl.MaxInputLength)
            .IsRequired();

        builder.Property(d => d.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(d => d.CreatedAt).IsRequired();
        builder.Property(d => d.UpdatedAt).IsRequired();
        builder.Property(d => d.ActivatedAt);
        builder.Property(d => d.DeactivatedAt);
    }
}
