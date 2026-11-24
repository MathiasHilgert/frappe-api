using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Principals;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Identity.Infrastructure.Codes;

/// <summary>Maps <see cref="OneTimeCode"/> to <c>one_time_codes</c>: at most one live code per principal and purpose.</summary>
internal sealed class OneTimeCodeConfiguration : IEntityTypeConfiguration<OneTimeCode>
{
    public const string Table = "one_time_codes";

    public const string PrimaryKey = "pk_one_time_codes";

    public const string ForeignKey = "fk_one_time_codes_principal_id";

    /// <summary>The longest address a code can target.</summary>
    public const int TargetLength = 254;

    public const string PurposeCheck = "ck_one_time_codes_purpose";

    /// <summary>A code hash is an HMAC-SHA256 value.</summary>
    public const string CodeHmacLength = "ck_one_time_codes_code_hmac_length";

    public const string CodeHmacLengthSql = "octet_length(code_hmac) = 32";

    /// <summary>A code expires after it was issued.</summary>
    public const string ExpiresAfterIssued = "ck_one_time_codes_expires_after_issued";

    public const string ExpiresAfterIssuedSql = "expires_at > issued_at";

    /// <summary>A code is redeemed inside its window, never before it was issued or after it expired.</summary>
    public const string RedeemedInWindow = "ck_one_time_codes_redeemed_in_window";

    public const string RedeemedInWindowSql = "redeemed_at IS NULL OR (redeemed_at >= issued_at AND redeemed_at <= expires_at)";

    /// <summary>A code is invalidated after it was issued.</summary>
    public const string InvalidatedAfterIssued = "ck_one_time_codes_invalidated_after_issued";

    public const string InvalidatedAfterIssuedSql = "invalidated_at IS NULL OR invalidated_at >= issued_at";

    /// <summary>Only one code per principal and purpose is live: neither redeemed nor invalidated.</summary>
    public const string LiveIndex = "ux_one_time_codes_live";

    public const string LiveFilter = "redeemed_at IS NULL AND invalidated_at IS NULL";

    /// <summary>The purge reads codes past their expiry.</summary>
    public const string ExpiresAtIndex = "ix_one_time_codes_expires_at";

    public void Configure(EntityTypeBuilder<OneTimeCode> builder)
    {
        builder.ToTable(Table, table =>
        {
            table.HasCheckConstraint(PurposeCheck, $"purpose IN ({StoredEnum<CodePurpose>.CheckList})");
            table.HasCheckConstraint(CodeHmacLength, CodeHmacLengthSql);
            table.HasCheckConstraint(ExpiresAfterIssued, ExpiresAfterIssuedSql);
            table.HasCheckConstraint(RedeemedInWindow, RedeemedInWindowSql);
            table.HasCheckConstraint(InvalidatedAfterIssued, InvalidatedAfterIssuedSql);
        });
        builder.HasKey(c => c.Id).HasName(PrimaryKey);
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsRowVersion();
        builder.Property(c => c.PrincipalId).HasColumnName("principal_id");
        builder.Property(c => c.Purpose).HasColumnName("purpose")
            .HasConversion(StoredEnum<CodePurpose>.Converter).HasMaxLength(StoredEnum<CodePurpose>.MaxLength);
        builder.Property(c => c.Target).HasColumnName("target").HasMaxLength(TargetLength);
        builder.Property(c => c.CodeHmac).HasColumnName("code_hmac");
        builder.Property(c => c.IssuedAt).HasColumnName("issued_at");
        builder.Property(c => c.ExpiresAt).HasColumnName("expires_at");
        builder.Property(c => c.RedeemedAt).HasColumnName("redeemed_at");
        builder.Property(c => c.InvalidatedAt).HasColumnName("invalidated_at");
        builder.HasOne<Principal>().WithMany().HasForeignKey(c => c.PrincipalId).HasConstraintName(ForeignKey).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.PrincipalId, c.Purpose }, LiveIndex).HasDatabaseName(LiveIndex).IsUnique().HasFilter(LiveFilter);
        builder.HasIndex(c => c.ExpiresAt).HasDatabaseName(ExpiresAtIndex);
    }
}
