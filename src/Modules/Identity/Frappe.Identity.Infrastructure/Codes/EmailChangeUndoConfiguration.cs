using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Principals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Frappe.Identity.Infrastructure.Codes;

/// <summary>Maps <see cref="EmailChangeUndo"/> to <c>email_change_undos</c>: at most one live token per principal and pair of addresses.</summary>
internal sealed class EmailChangeUndoConfiguration : IEntityTypeConfiguration<EmailChangeUndo>
{
    public const string Table = "email_change_undos";

    public const string PrimaryKey = "pk_email_change_undos";

    public const string ForeignKey = "fk_email_change_undos_principal_id";

    public const string TokenHmacLength = "ck_email_change_undos_token_hmac_length";

    public const string ExpiresAfterIssued = "ck_email_change_undos_expires_after_issued";

    public const string RedeemedInWindow = "ck_email_change_undos_redeemed_in_window";

    public const string InvalidatedAfterIssued = "ck_email_change_undos_invalidated_after_issued";

    /// <summary>Only one token per principal and change (old and new address) is live: neither redeemed nor invalidated.</summary>
    public const string LiveIndex = "ux_email_change_undos_live";

    /// <summary>The purge reads tokens past their expiry.</summary>
    public const string ExpiresAtIndex = "ix_email_change_undos_expires_at";

    // Stored as the 32 bytes; the comparer reads them, so a copy of an unchanged value is never seen as a change.
    private static readonly ValueConverter<CodeHmac, byte[]> TokenHmacConverter = new(
        hmac => hmac.ToArray(), bytes => CodeHmac.Create(bytes).Value);

    public void Configure(EntityTypeBuilder<EmailChangeUndo> builder)
    {
        builder.ToTable(Table, table =>
        {
            table.HasCheckConstraint(
                TokenHmacLength, OneTimeCodeConfiguration.CodeHmacLengthSql.Replace("code_hmac", "token_hmac", StringComparison.Ordinal));
            table.HasCheckConstraint(ExpiresAfterIssued, OneTimeCodeConfiguration.ExpiresAfterIssuedSql);
            table.HasCheckConstraint(RedeemedInWindow, OneTimeCodeConfiguration.RedeemedInWindowSql);
            table.HasCheckConstraint(InvalidatedAfterIssued, OneTimeCodeConfiguration.InvalidatedAfterIssuedSql);
        });
        builder.HasKey(u => u.Id).HasName(PrimaryKey);
        builder.Property(u => u.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsRowVersion();
        builder.Property(u => u.PrincipalId).HasColumnName("principal_id");
        builder.Property(u => u.OldAddress).HasColumnName("old_address").HasMaxLength(OneTimeCodeConfiguration.TargetLength);
        builder.Property(u => u.NewAddress).HasColumnName("new_address").HasMaxLength(OneTimeCodeConfiguration.TargetLength);
        builder.Property(u => u.TokenHmac).HasColumnName("token_hmac").HasConversion(TokenHmacConverter);
        builder.Property(u => u.IssuedAt).HasColumnName("issued_at");
        builder.Property(u => u.ExpiresAt).HasColumnName("expires_at");
        builder.Property(u => u.RedeemedAt).HasColumnName("redeemed_at");
        builder.Property(u => u.InvalidatedAt).HasColumnName("invalidated_at");
        builder.HasOne<Principal>().WithMany().HasForeignKey(u => u.PrincipalId).HasConstraintName(ForeignKey).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(u => new { u.PrincipalId, u.OldAddress, u.NewAddress }, LiveIndex).HasDatabaseName(LiveIndex).IsUnique()
            .HasFilter(OneTimeCodeConfiguration.LiveFilter);
        builder.HasIndex(u => u.ExpiresAt).HasDatabaseName(ExpiresAtIndex);
    }
}
