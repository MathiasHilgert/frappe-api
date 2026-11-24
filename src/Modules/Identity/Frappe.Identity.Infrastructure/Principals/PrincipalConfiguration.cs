using Frappe.Identity.Domain.Principals;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Identity.Infrastructure.Principals;

/// <summary>Maps <see cref="Principal"/> to <c>principals</c>, the root every other table of the module hangs from.</summary>
internal sealed class PrincipalConfiguration : IEntityTypeConfiguration<Principal>
{
    public const string Table = "principals";

    public const string PrimaryKey = "pk_principals";

    public const string KindCheck = "ck_principals_kind";

    public const string StatusCheck = "ck_principals_status";

    public void Configure(EntityTypeBuilder<Principal> builder)
    {
        builder.ToTable(Table, table =>
        {
            table.HasCheckConstraint(KindCheck, $"kind IN ({StoredEnum<PrincipalKind>.CheckList})");
            table.HasCheckConstraint(StatusCheck, $"status IN ({StoredEnum<PrincipalStatus>.CheckList})");
        });
        builder.HasKey(p => p.Id).HasName(PrimaryKey);
        builder.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsRowVersion();
        builder.Property(p => p.Kind).HasColumnName("kind")
            .HasConversion(StoredEnum<PrincipalKind>.Converter).HasMaxLength(StoredEnum<PrincipalKind>.MaxLength);
        builder.Property(p => p.Status).HasColumnName("status")
            .HasConversion(StoredEnum<PrincipalStatus>.Converter).HasMaxLength(StoredEnum<PrincipalStatus>.MaxLength);
        builder.Property(p => p.CredentialStamp).HasColumnName("credential_stamp");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
    }
}
