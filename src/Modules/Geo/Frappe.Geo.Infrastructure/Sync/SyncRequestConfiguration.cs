using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Geo.Infrastructure.Sync;

internal sealed class SyncRequestConfiguration : IEntityTypeConfiguration<SyncRequestRow>
{
    public void Configure(EntityTypeBuilder<SyncRequestRow> builder)
    {
        builder.ToTable("sync_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.SourceId).HasColumnName("source_id").HasMaxLength(64);
        builder.Property(r => r.RequestedAt).HasColumnName("requested_at");
    }
}
