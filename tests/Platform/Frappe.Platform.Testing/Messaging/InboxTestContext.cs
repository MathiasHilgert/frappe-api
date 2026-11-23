using Frappe.Messaging.Inbox;
using Frappe.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Platform.Testing.Messaging;

/// <summary>A module's context as the messaging platform sees it: its outbox and inbox, and one table a handler writes to.</summary>
public sealed class InboxTestContext(DbContextOptions<InboxTestContext> options) : DbContext(options)
{
    public DbSet<HandledEffect> Effects => Set<HandledEffect>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("module");
        modelBuilder.AddOutbox();
        modelBuilder.AddInbox();
        modelBuilder.Entity<HandledEffect>().ToTable("effects").HasKey(e => e.Id);
    }
}
