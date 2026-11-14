using Microsoft.EntityFrameworkCore;

namespace Frappe.Messaging.Outbox;

public static class OutboxModel
{
    public const string Table = "outbox_messages";

    /// <summary>W3C Trace Context asks vendors to propagate at least 512 characters of tracestate.</summary>
    public const int MaxTraceStateLength = 512;

    public const int MaxErrorLength = 2000;

    /// <summary>Adds the outbox table to a module model; it lands in the module's default schema.</summary>
    public static ModelBuilder AddOutbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<OutboxMessage>(outbox =>
        {
            outbox.ToTable(Table);
            outbox.HasKey(m => m.Id);
            outbox.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
            outbox.Property(m => m.Sequence).HasColumnName("sequence").UseIdentityAlwaysColumn();
            outbox.Property(m => m.Type).HasColumnName("type").HasMaxLength(200);
            outbox.Property(m => m.Source).HasColumnName("source").HasMaxLength(200);
            outbox.Property(m => m.DataContentType).HasColumnName("data_content_type").HasMaxLength(100);
            outbox.Property(m => m.Data).HasColumnName("data").HasColumnType("jsonb");
            outbox.Property(m => m.OccurredAt).HasColumnName("occurred_at");
            outbox.Property(m => m.TraceParent).HasColumnName("trace_parent").HasMaxLength(55);
            outbox.Property(m => m.TraceState).HasColumnName("trace_state").HasMaxLength(MaxTraceStateLength);
            outbox.Property(m => m.PublishedAt).HasColumnName("published_at");
            outbox.Property(m => m.Attempts).HasColumnName("attempts");
            outbox.Property(m => m.NextAttemptAt).HasColumnName("next_attempt_at");
            outbox.Property(m => m.LeasedUntil).HasColumnName("leased_until").IsConcurrencyToken();
            outbox.Property(m => m.LastError).HasColumnName("last_error").HasMaxLength(MaxErrorLength);

            // The relay only ever looks for pending messages, in insertion order.
            outbox.HasIndex(m => m.Sequence)
                .HasDatabaseName("ix_outbox_messages_pending")
                .HasFilter("published_at IS NULL");

            // The retention purge only ever looks for published messages older than a cutoff.
            outbox.HasIndex(m => m.PublishedAt)
                .HasDatabaseName("ix_outbox_messages_published_at")
                .HasFilter("published_at IS NOT NULL");
        });
        return modelBuilder;
    }
}
