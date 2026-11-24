using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Sessions;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Identity.Infrastructure.Sessions;

/// <summary>
/// Maps <see cref="Session"/> to <c>sessions</c>. Authentication touches a session at most once a minute, so no index covers the instants that
/// change (<c>last_seen_at</c>, the expiries): an update then stays a heap-only tuple.
/// </summary>
internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public const string Table = "sessions";

    public const string PrimaryKey = "pk_sessions";

    public const string ForeignKey = "fk_sessions_principal_id";

    /// <summary>The longest user agent kept ("El texto completo").</summary>
    public const int UserAgentLength = 512;

    public const string AudienceCheck = "ck_sessions_audience";

    public const string RevocationReasonCheck = "ck_sessions_revocation_reason";

    /// <summary>A token hash is a SHA-256 value.</summary>
    public const string TokenHashLength = "ck_sessions_token_hash_length";

    public const string TokenHashLengthSql = "octet_length(token_hash) = 32";

    /// <summary>A session never idles out later than its absolute expiry.</summary>
    public const string IdleWithinAbsolute = "ck_sessions_idle_within_absolute";

    public const string IdleWithinAbsoluteSql = "idle_expires_at <= absolute_expires_at";

    /// <summary>A session is not seen before it opened.</summary>
    public const string SeenAfterOpened = "ck_sessions_seen_after_opened";

    public const string SeenAfterOpenedSql = "opened_at <= last_seen_at";

    /// <summary>A session is revoked exactly when it has the reason.</summary>
    public const string RevocationPair = "ck_sessions_revocation_pair";

    public const string RevocationPairSql = "(revoked_at IS NULL) = (revocation_reason IS NULL)";

    /// <summary>A session ends after it was revoked, never before (a null on either side does not apply).</summary>
    public const string EndedAfterRevoked = "ck_sessions_ended_after_revoked";

    public const string EndedAfterRevokedSql = "ended_at >= revoked_at";

    /// <summary>The methods are a non-empty subset of the known ones.</summary>
    public const string MethodsCheck = "ck_sessions_methods";

    /// <summary>Only the truncated address is kept: an IPv4 /24 or an IPv6 /48.</summary>
    public const string IpPrefixCheck = "ck_sessions_ip_prefix";

    public const string IpPrefixCheckSql =
        "ip_prefix IS NULL OR (family(ip_prefix) = 4 AND masklen(ip_prefix) = 24) OR (family(ip_prefix) = 6 AND masklen(ip_prefix) = 48)";

    /// <summary>A token hash identifies one session; authentication looks it up by this index.</summary>
    public const string TokenHashIndex = "ux_sessions_token_hash";

    /// <summary>A principal's sessions: listing, revoking the others and the credential change read them. Full, not partial.</summary>
    public const string PrincipalIndex = "ix_sessions_principal_id";

    /// <summary>The purge reads ended sessions older than a cutoff; a live session is not in it.</summary>
    public const string EndedAtIndex = "ix_sessions_ended_at";

    public const string EndedAtFilter = "ended_at IS NOT NULL";

    public static string MethodsCheckSql { get; } =
        $"methods <@ ARRAY[{StoredEnum<AuthenticationMethod>.CheckList}]::text[] AND cardinality(methods) > 0";

    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable(Table, table =>
        {
            table.HasCheckConstraint(AudienceCheck, $"audience IN ({StoredEnum<SessionAudience>.CheckList})");
            table.HasCheckConstraint(RevocationReasonCheck, $"revocation_reason IN ({StoredEnum<RevocationReason>.CheckList})");
            table.HasCheckConstraint(TokenHashLength, TokenHashLengthSql);
            table.HasCheckConstraint(IdleWithinAbsolute, IdleWithinAbsoluteSql);
            table.HasCheckConstraint(SeenAfterOpened, SeenAfterOpenedSql);
            table.HasCheckConstraint(RevocationPair, RevocationPairSql);
            table.HasCheckConstraint(EndedAfterRevoked, EndedAfterRevokedSql);
            table.HasCheckConstraint(MethodsCheck, MethodsCheckSql);
            table.HasCheckConstraint(IpPrefixCheck, IpPrefixCheckSql);
        });
        builder.HasKey(s => s.Id).HasName(PrimaryKey);
        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsRowVersion();
        builder.Property(s => s.PrincipalId).HasColumnName("principal_id");
        builder.Property(s => s.TokenHash).HasColumnName("token_hash");
        builder.Property(s => s.Audience).HasColumnName("audience")
            .HasConversion(StoredEnum<SessionAudience>.Converter).HasMaxLength(StoredEnum<SessionAudience>.MaxLength);
        builder.PrimitiveCollection(s => s.Methods).HasColumnName("methods").ElementType()
            .HasConversion(StoredEnum<AuthenticationMethod>.Converter);
        builder.Property(s => s.CredentialStamp).HasColumnName("credential_stamp");
        builder.Property(s => s.OpenedAt).HasColumnName("opened_at");
        builder.Property(s => s.LastSeenAt).HasColumnName("last_seen_at");
        builder.Property(s => s.IdleExpiresAt).HasColumnName("idle_expires_at");
        builder.Property(s => s.AbsoluteExpiresAt).HasColumnName("absolute_expires_at");
        builder.Property(s => s.EndedAt).HasColumnName("ended_at");
        builder.Property(s => s.UserAgent).HasColumnName("user_agent").HasMaxLength(UserAgentLength);
        builder.Property(s => s.IpPrefix).HasColumnName("ip_prefix").HasColumnType("cidr");
        builder.Property(s => s.StepUpUntil).HasColumnName("step_up_until");
        builder.Property(s => s.RevokedAt).HasColumnName("revoked_at");
        builder.Property(s => s.RevocationReason).HasColumnName("revocation_reason")
            .HasConversion(StoredEnum<RevocationReason>.Converter).HasMaxLength(StoredEnum<RevocationReason>.MaxLength);
        builder.HasOne<Principal>().WithMany().HasForeignKey(s => s.PrincipalId).HasConstraintName(ForeignKey).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => s.TokenHash, TokenHashIndex).HasDatabaseName(TokenHashIndex).IsUnique();
        builder.HasIndex(s => s.PrincipalId).HasDatabaseName(PrincipalIndex);
        builder.HasIndex(s => s.EndedAt).HasDatabaseName(EndedAtIndex).HasFilter(EndedAtFilter);
    }
}
