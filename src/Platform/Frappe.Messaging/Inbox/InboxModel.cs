using Microsoft.EntityFrameworkCore;

namespace Frappe.Messaging.Inbox;

/// <summary>Names and shapes the inbox table every module owns, so each module maps it the same way.</summary>
public static class InboxModel
{
    extension(ModelBuilder modelBuilder)
    {
        /// <summary>Adds the inbox table to a module model; it lands in the module's default schema.</summary>
        public ModelBuilder AddInbox()
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);
            modelBuilder.Entity<InboxMessage>(inbox =>
            {
                inbox.ToTable(Table);
                inbox.HasKey(m => new { m.Consumer, m.MessageId }).HasName(PrimaryKey);
                inbox.Property(m => m.Consumer).HasColumnName("consumer").HasMaxLength(ConsumerLength);
                inbox.Property(m => m.MessageId).HasColumnName("message_id");
                inbox.Property(m => m.ReceivedAt).HasColumnName("received_at");

                // The retention purge looks for records older than a cutoff.
                inbox.HasIndex(m => m.ReceivedAt).HasDatabaseName("ix_inbox_messages_received_at");
            });
            return modelBuilder;
        }
    }

    public const string Table = "inbox_messages";

    /// <summary>The primary key's name, as every module's migration renames it to (DATA-05).</summary>
    public const string PrimaryKey = "pk_inbox_messages";

    /// <summary>The longest consumer name the inbox stores.</summary>
    public const int ConsumerLength = 100;
}
