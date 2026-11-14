using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Geo.Infrastructure.Sync;

internal sealed class SyncRunConfiguration : IEntityTypeConfiguration<SyncRunRow>
{
    public void Configure(EntityTypeBuilder<SyncRunRow> builder)
    {
        builder.ToTable("sync_runs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.SourceId).HasColumnName("source_id").HasMaxLength(64);
        builder.Property(r => r.StartedAt).HasColumnName("started_at");
        builder.Property(r => r.FinishedAt).HasColumnName("finished_at");
        builder.Property(r => r.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.Inserted).HasColumnName("inserted");
        builder.Property(r => r.Updated).HasColumnName("updated");
        builder.Property(r => r.Retired).HasColumnName("retired");
        builder.Property(r => r.Revived).HasColumnName("revived");
        builder.Property(r => r.Error).HasColumnName("error").HasMaxLength(SyncRunRow.MaxErrorLength);
        // A complex collection in one jsonb column: EF Core 11 obsoletes owned entity collections mapped to JSON (and their
        // synthesized ordinal key). The stored JSON is the same array of objects keyed by property name.
        builder.ComplexCollection(r => r.Conflicts, conflicts => conflicts.ToJson("conflicts"));
        builder.HasIndex(r => new { r.SourceId, r.StartedAt }).HasDatabaseName("ix_sync_runs_source_started").IsDescending(false, true);
    }
}
