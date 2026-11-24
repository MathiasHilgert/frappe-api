using Frappe.Identity.Domain.Owners;
using Frappe.Identity.Domain.Principals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Identity.Infrastructure.Owners;

/// <summary>Maps <see cref="Owner"/> to <c>owners</c>, keyed by its principal; the core's tables never refer to it.</summary>
internal sealed class OwnerConfiguration : IEntityTypeConfiguration<Owner>
{
    public const string Table = "owners";

    public const string PrimaryKey = "pk_owners";

    public const string ForeignKey = "fk_owners_principal_id";

    public const int NameLength = 200;

    /// <summary>An owner is verified after it registered, never before.</summary>
    public const string VerifiedAfterRegistered = "ck_owners_verified_after_registered";

    public const string VerifiedAfterRegisteredSql = "verified_at >= registered_at";

    /// <summary>The purge reads pending owners (not verified) older than a cutoff; a verified owner is not in it.</summary>
    public const string RegisteredAtIndex = "ix_owners_registered_at";

    public const string RegisteredAtFilter = "verified_at IS NULL";

    public void Configure(EntityTypeBuilder<Owner> builder)
    {
        builder.ToTable(Table, table => table.HasCheckConstraint(VerifiedAfterRegistered, VerifiedAfterRegisteredSql));
        builder.HasKey(o => o.PrincipalId).HasName(PrimaryKey);
        builder.Property(o => o.PrincipalId).HasColumnName("principal_id").ValueGeneratedNever();
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsRowVersion();
        builder.Property(o => o.GivenName).HasColumnName("given_name").HasMaxLength(NameLength);
        builder.Property(o => o.FamilyName).HasColumnName("family_name").HasMaxLength(NameLength);
        builder.Property(o => o.Locale).HasColumnName("locale");
        builder.Property(o => o.RegisteredAt).HasColumnName("registered_at");
        builder.Property(o => o.VerifiedAt).HasColumnName("verified_at");
        builder.HasOne<Principal>().WithOne().HasForeignKey<Owner>(o => o.PrincipalId).HasConstraintName(ForeignKey).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(o => o.RegisteredAt).HasDatabaseName(RegisteredAtIndex).HasFilter(RegisteredAtFilter);
    }
}
