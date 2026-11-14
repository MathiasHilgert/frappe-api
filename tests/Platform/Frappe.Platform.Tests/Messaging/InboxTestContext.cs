using Frappe.Messaging.Inbox;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Platform.Tests.Messaging;

/// <summary>A module's context as the inbox sees it: its inbox, and one table a handler writes to.</summary>
public sealed class InboxTestContext(DbContextOptions<InboxTestContext> options) : DbContext(options)
{
    public DbSet<HandledEffect> Effects => Set<HandledEffect>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("module");
        modelBuilder.AddInbox();
        modelBuilder.Entity<HandledEffect>().ToTable("effects").HasKey(e => e.Id);
    }
}
