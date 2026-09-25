using EcoBilling.Control.Domain.Administrators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoBilling.Control.Infrastructure.Persistence.Administrators;

/// <summary>Maps <see cref="RefreshToken"/> onto the <c>RefreshTokens</c> table.</summary>
public sealed class RefreshTokenEntityConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    // Convert.ToHexString(SHA256.HashData(...)) always produces exactly 64 hex
    // characters (32 bytes), unlike a password hash's length, which is not fixed by
    // this codebase and is deliberately left unbounded (see AdministratorEntityConfiguration).
    private const int TokenHashLength = 64;

    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasConversion(id => id.Value, value => new RefreshTokenId(value))
            .ValueGeneratedNever();

        builder.Property(t => t.AdministratorId)
            .HasConversion(id => id.Value, value => new AdministratorId(value))
            .IsRequired();

        // No navigation property on either side (Domain doesn't need one); this is a
        // property-only foreign key, still enforced as a real FK constraint at the
        // database level.
        builder.HasOne<Administrator>()
            .WithMany()
            .HasForeignKey(t => t.AdministratorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(t => t.TokenHash)
            .HasMaxLength(TokenHashLength)
            .IsFixedLength()
            .IsRequired();

        builder.HasIndex(t => t.TokenHash)
            .IsUnique();

        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.ExpiresAt).IsRequired();
        builder.Property(t => t.RevokedAt);

        builder.Property(t => t.ReplacedByTokenId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new RefreshTokenId(value.Value) : (RefreshTokenId?)null);
    }
}
