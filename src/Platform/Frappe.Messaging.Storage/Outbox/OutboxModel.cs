using Microsoft.EntityFrameworkCore;

namespace Frappe.Messaging.Storage.Outbox;

/// <summary>Names and shapes the outbox table every module owns, so each module maps it the same way.</summary>
public static class OutboxModel
{
    extension(ModelBuilder modelBuilder)
    {
        /// <summary>Adds the outbox table to a module model; it lands in the module's default schema.</summary>
        public ModelBuilder AddOutbox()
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);
            modelBuilder.Entity<OutboxMessage>(outbox =>
            {
                // The constraint belongs to the table, so it is declared through the table builder (the entity-level overload is obsolete).
                outbox.ToTable(Table, table =>
                {
                    foreach (var (name, sql) in Invariants)
                    {
                        table.HasCheckConstraint(name, sql);
                    }
                });
                outbox.HasKey(m => m.Id).HasName(PrimaryKey);
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

                // The relay only ever claims pending messages, in insertion order; parked ones have their own small index,
                // so the claim never steps over them. Both are named, as two indexes on one column must be.
                outbox.HasIndex(m => m.Sequence, PendingIndex)
                    .HasDatabaseName(PendingIndex)
                    .HasFilter(PendingFilter);
                outbox.HasIndex(m => m.Sequence, ParkedIndex)
                    .HasDatabaseName(ParkedIndex)
                    .HasFilter(ParkedFilter);

                // The retention purge only ever looks for published messages older than a cutoff.
                outbox.HasIndex(m => m.PublishedAt)
                    .HasDatabaseName("ix_outbox_messages_published_at")
                    .HasFilter("published_at IS NOT NULL");
            });
            return modelBuilder;
        }
    }

    public const string Table = "outbox_messages";

    /// <summary>The primary key's name, as every module's migration renames it to (DATA-05).</summary>
    public const string PrimaryKey = "pk_outbox_messages";

    /// <summary>W3C Trace Context asks vendors to propagate at least 512 characters of tracestate.</summary>
    public const int MaxTraceStateLength = 512;

    public const int MaxErrorLength = 2000;

    /// <summary>
    /// Rows the relay may still publish: unpublished and not parked. A parked row (<see cref="ParkedFilter"/>) is never
    /// claimed again, so it stays out of <see cref="PendingIndex"/> and the claim never walks it. The relay's claim
    /// repeats this text word for word: Postgres only uses a partial index when the query's filter proves its predicate.
    /// </summary>
    public const string PendingFilter = "published_at IS NULL AND (attempts = 0 OR next_attempt_at IS NOT NULL)";

    /// <summary>
    /// Rows parked after their last allowed attempt: unpublished, failed at least once and given no next attempt time
    /// (<see cref="OutboxMessage.NextAttemptAt"/> is null only when a row never failed or is parked). No configuration is
    /// part of it, so the index stays valid whatever <c>MaxAttempts</c> a module runs with.
    /// </summary>
    public const string ParkedFilter = "published_at IS NULL AND attempts > 0 AND next_attempt_at IS NULL";

    /// <summary>The relay's claim reads only this index, in insertion order: the rows of <see cref="PendingFilter"/>.</summary>
    public const string PendingIndex = "ix_outbox_messages_relayable";

    /// <summary>The parked rows, in insertion order: the relay's parked count and oldest parked row read only them.</summary>
    public const string ParkedIndex = "ix_outbox_messages_parked";

    /// <summary>I1: a row never has a negative attempt count.</summary>
    public const string AttemptsNonNegative = "ck_outbox_messages_attempts_nonnegative";

    public const string AttemptsNonNegativeSql = "attempts >= 0";

    /// <summary>I2: only a row that failed at least once has a next attempt time.</summary>
    public const string NextAttemptAfterFailure = "ck_outbox_messages_next_attempt_after_failure";

    public const string NextAttemptAfterFailureSql = "next_attempt_at IS NULL OR attempts > 0";

    /// <summary>I3: only a row that failed at least once has a last error.</summary>
    public const string LastErrorAfterFailure = "ck_outbox_messages_last_error_after_failure";

    public const string LastErrorAfterFailureSql = "last_error IS NULL OR attempts > 0";

    /// <summary>
    /// I4: a published row holds no lease and no error. Deliberately not required: "published implies no next attempt
    /// time", which the relay breaks today (a message published after a retry keeps its <c>next_attempt_at</c>).
    /// </summary>
    public const string PublishedSettled = "ck_outbox_messages_published_settled";

    public const string PublishedSettledSql = "published_at IS NULL OR (leased_until IS NULL AND last_error IS NULL)";

    /// <summary>The four invariants every outbox enforces, as (constraint name, predicate); the model and the migrations both read this.</summary>
    public static IReadOnlyList<(string Name, string Sql)> Invariants { get; } =
    [
        (AttemptsNonNegative, AttemptsNonNegativeSql),
        (NextAttemptAfterFailure, NextAttemptAfterFailureSql),
        (LastErrorAfterFailure, LastErrorAfterFailureSql),
        (PublishedSettled, PublishedSettledSql),
    ];
}
