using Microsoft.EntityFrameworkCore;

namespace Frappe.Messaging.Inbox;

public static class InboxModel
{
    public const string Table = "inbox_messages";

    /// <summary>Adds the inbox table to a module model; it lands in the module's default schema.</summary>
    public static ModelBuilder AddInbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<InboxMessage>(inbox =>
        {
            inbox.ToTable(Table);
            inbox.HasKey(m => new { m.Consumer, m.MessageId });
            inbox.Property(m => m.Consumer).HasColumnName("consumer").HasMaxLength(100);
            inbox.Property(m => m.MessageId).HasColumnName("message_id");
            inbox.Property(m => m.ReceivedAt).HasColumnName("received_at");

            // The retention purge looks for records older than a cutoff.
            inbox.HasIndex(m => m.ReceivedAt).HasDatabaseName("ix_inbox_messages_received_at");
        });
        return modelBuilder;
    }
}
