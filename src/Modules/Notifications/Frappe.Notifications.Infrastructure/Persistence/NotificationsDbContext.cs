using Frappe.Messaging.Storage.Inbox;
using Frappe.Messaging.Storage.Outbox;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.Preferences;
using Frappe.Notifications.Domain.Requests;
using Frappe.Notifications.Domain.Suppressions;
using Frappe.Notifications.Infrastructure.ProviderEvents;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Notifications.Infrastructure.Persistence;

/// <summary>
/// The Notifications module's database: its tables in schema <see cref="Schema"/>, with the outbox and the inbox every module owns
/// (DATA-13). It holds only mapping.
/// </summary>
internal sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    /// <summary>The Postgres schema the module owns; it also holds its migration history (DATA-01).</summary>
    public const string Schema = "notifications";

    public DbSet<Delivery> Deliveries => Set<Delivery>();

    public DbSet<Suppression> Suppressions => Set<Suppression>();

    public DbSet<Preference> Preferences => Set<Preference>();

    public DbSet<ProviderEventRow> ProviderEvents => Set<ProviderEventRow>();

    public DbSet<SendRequest> SendRequests => Set<SendRequest>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) => configurationBuilder.StoreLanguageTags();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly);
        modelBuilder.AddOutbox();
        modelBuilder.AddInbox();
    }
}
