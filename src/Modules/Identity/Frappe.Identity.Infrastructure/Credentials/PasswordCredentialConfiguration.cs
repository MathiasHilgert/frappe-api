using Frappe.Identity.Domain.Credentials;
using Frappe.Identity.Domain.Principals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Identity.Infrastructure.Credentials;

/// <summary>Maps <see cref="PasswordCredential"/> to <c>password_credentials</c>, keyed by its principal.</summary>
internal sealed class PasswordCredentialConfiguration : IEntityTypeConfiguration<PasswordCredential>
{
    public const string Table = "password_credentials";

    public const string PrimaryKey = "pk_password_credentials";

    public const string ForeignKey = "fk_password_credentials_principal_id";

    /// <summary>Room for the encoded Argon2id hash with its parameters and salt, and for a stronger scheme later.</summary>
    public const int HashLength = 255;

    public void Configure(EntityTypeBuilder<PasswordCredential> builder)
    {
        builder.ToTable(Table);
        builder.HasKey(c => c.PrincipalId).HasName(PrimaryKey);
        builder.Property(c => c.PrincipalId).HasColumnName("principal_id").ValueGeneratedNever();
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsRowVersion();
        builder.Property(c => c.Hash).HasColumnName("hash").HasMaxLength(HashLength);
        builder.Property(c => c.ChangedAt).HasColumnName("changed_at");
        builder.HasOne<Principal>().WithOne().HasForeignKey<PasswordCredential>(c => c.PrincipalId).HasConstraintName(ForeignKey)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
