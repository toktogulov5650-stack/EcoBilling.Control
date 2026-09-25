using EcoBilling.Control.Domain.Administrators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoBilling.Control.Infrastructure.Persistence.Administrators;

/// <summary>Maps <see cref="Administrator"/> onto the <c>Administrators</c> table.</summary>
/// <remarks>
/// <see cref="Administrator"/>'s private parameterless constructor and private-set
/// properties are materialized the same way <c>District</c>'s are (Stage 4): EF Core
/// writes each property's backing field directly, bypassing accessors entirely.
/// </remarks>
public sealed class AdministratorEntityConfiguration : IEntityTypeConfiguration<Administrator>
{
    public void Configure(EntityTypeBuilder<Administrator> builder)
    {
        builder.ToTable("Administrators");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasConversion(id => id.Value, value => new AdministratorId(value))
            .ValueGeneratedNever();

        builder.Property(a => a.Email)
            .HasMaxLength(AdministratorEmail.MaxInputLength)
            .IsRequired();

        builder.Property(a => a.NormalizedEmail)
            .HasMaxLength(AdministratorEmail.MaxInputLength)
            .IsRequired();

        builder.HasIndex(a => a.NormalizedEmail)
            .IsUnique();

        builder.Property(a => a.FullName)
            .HasMaxLength(Administrator.MaxFullNameLength)
            .IsRequired();

        // Never returned through any API response (brief's mandate) -- enforced by
        // never including it in a DTO, not by any restriction here; EF Core needs the
        // real, full value to write and read it back correctly.
        builder.Property(a => a.PasswordHash)
            .IsRequired();

        builder.Property(a => a.IsActive)
            .IsRequired();

        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.UpdatedAt).IsRequired();
        builder.Property(a => a.LastLoginAt);
    }
}
