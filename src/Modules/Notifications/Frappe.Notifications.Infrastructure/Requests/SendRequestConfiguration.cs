using Frappe.Notifications.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Notifications.Infrastructure.Requests;

/// <summary>Maps <see cref="SendRequest"/> to <c>send_requests</c>: identity, kind and instant, and never the payload (S13).</summary>
internal sealed class SendRequestConfiguration : IEntityTypeConfiguration<SendRequest>
{
    public const string Table = "send_requests";

    public const string PrimaryKey = "pk_send_requests";

    /// <summary>A kind is lower snake case, starting with a letter.</summary>
    public const string KindFormat = "ck_send_requests_kind_format";

    public const string KindFormatSql = "kind ~ '^[a-z][a-z0-9_]*$'";

    /// <summary>The purge reads requests older than a cutoff. N7 adds the purge by <c>requested_at</c> (S11); its role needs DELETE.</summary>
    public const string RequestedAtIndex = "ix_send_requests_requested_at";

    public const int KindLength = 100;

    public void Configure(EntityTypeBuilder<SendRequest> builder)
    {
        builder.ToTable(Table, table => table.HasCheckConstraint(KindFormat, KindFormatSql));
        builder.HasKey(r => r.Id).HasName(PrimaryKey);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.Kind).HasColumnName("kind").HasMaxLength(KindLength);
        builder.Property(r => r.RequestedAt).HasColumnName("requested_at");
        builder.HasIndex(r => r.RequestedAt).HasDatabaseName(RequestedAtIndex);
    }
}
