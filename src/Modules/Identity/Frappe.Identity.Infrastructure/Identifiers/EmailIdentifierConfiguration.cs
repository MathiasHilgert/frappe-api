using Frappe.Identity.Domain.Identifiers;
using Frappe.Identity.Domain.Principals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Identity.Infrastructure.Identifiers;

/// <summary>Maps <see cref="EmailIdentifier"/> to <c>email_identifiers</c>: one address per principal, one principal per normalized one.</summary>
internal sealed class EmailIdentifierConfiguration : IEntityTypeConfiguration<EmailIdentifier>
{
    public const string Table = "email_identifiers";

    public const string PrimaryKey = "pk_email_identifiers";

    public const string ForeignKey = "fk_email_identifiers_principal_id";

    /// <summary>The longest address of RFC 5321.</summary>
    public const int AddressLength = 254;

    /// <summary>
    /// A normalized address belongs to one principal. A registration that loses the race for it hits this index, and the use case answers it as
    /// it answers a taken address.
    /// </summary>
    public const string NormalizedAddressIndex = "ux_email_identifiers_normalized_address";

    /// <summary>A principal has one address.</summary>
    public const string PrincipalIndex = "ux_email_identifiers_principal_id";

    public void Configure(EntityTypeBuilder<EmailIdentifier> builder)
    {
        builder.ToTable(Table);
        builder.HasKey(e => e.Id).HasName(PrimaryKey);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsRowVersion();
        builder.Property(e => e.PrincipalId).HasColumnName("principal_id");
        builder.Property(e => e.Address).HasColumnName("address").HasMaxLength(AddressLength);

        // A copy on purpose (DATA-12): the trimmed, lower-cased address, so the unique index and the log-in lookup compare plain text. The Email
        // value object makes both from the same input and the identifier is written once with them; a change of address replaces both.
        builder.Property(e => e.NormalizedAddress).HasColumnName("normalized_address").HasMaxLength(AddressLength)
            .HasComment("DATA-12 copy: address trimmed and lower-cased; written with it by the Email value object, never updated alone.");
        builder.Property(e => e.VerifiedAt).HasColumnName("verified_at");
        builder.HasOne<Principal>().WithMany().HasForeignKey(e => e.PrincipalId).HasConstraintName(ForeignKey).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.NormalizedAddress, NormalizedAddressIndex).HasDatabaseName(NormalizedAddressIndex).IsUnique();
        builder.HasIndex(e => e.PrincipalId, PrincipalIndex).HasDatabaseName(PrincipalIndex).IsUnique();
    }
}
