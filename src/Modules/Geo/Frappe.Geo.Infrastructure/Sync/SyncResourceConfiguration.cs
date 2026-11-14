using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Geo.Infrastructure.Sync;

internal sealed class SyncResourceConfiguration : IEntityTypeConfiguration<SyncResourceRow>
{
    public void Configure(EntityTypeBuilder<SyncResourceRow> builder)
    {
        builder.ToTable("sync_resources");
        builder.HasKey(r => new { r.SourceId, r.Resource });
        builder.Property(r => r.SourceId).HasColumnName("source_id").HasMaxLength(64);
        builder.Property(r => r.Resource).HasColumnName("resource").HasMaxLength(64);
        builder.Property(r => r.Kind).HasColumnName("kind").HasMaxLength(32);
        builder.Property(r => r.ETag).HasColumnName("etag").HasMaxLength(256);
        builder.Property(r => r.LastModified).HasColumnName("last_modified");
        builder.Property(r => r.Sha256).HasColumnName("sha256").HasMaxLength(64);
        builder.Property(r => r.LastAttemptAt).HasColumnName("last_attempt_at");
        builder.Property(r => r.LastSuccessAt).HasColumnName("last_success_at");
        builder.Property(r => r.LastOutcome).HasColumnName("last_outcome").HasMaxLength(32);
    }
}
