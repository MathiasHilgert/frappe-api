using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Credentials;
using Frappe.Identity.Domain.Identifiers;
using Frappe.Identity.Domain.Owners;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Sessions;
using Frappe.Messaging.Storage.Inbox;
using Frappe.Messaging.Storage.Outbox;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.Infrastructure.Persistence;

/// <summary>
/// The Identity module's database: its tables in schema <see cref="Schema"/>, with the outbox and the inbox every module owns (DATA-13). It
/// holds only mapping; the core's and the Owners feature's configurations sit next to the types they map (STR-09).
/// </summary>
internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    /// <summary>The Postgres schema the module owns; it also holds its migration history (DATA-01).</summary>
    public const string Schema = "identity";

    public DbSet<Principal> Principals => Set<Principal>();

    public DbSet<EmailIdentifier> EmailIdentifiers => Set<EmailIdentifier>();

    public DbSet<PasswordCredential> PasswordCredentials => Set<PasswordCredential>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<OneTimeCode> OneTimeCodes => Set<OneTimeCode>();

    public DbSet<EmailChangeUndo> EmailChangeUndos => Set<EmailChangeUndo>();

    public DbSet<Owner> Owners => Set<Owner>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) => configurationBuilder.StoreLanguageTags();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
        modelBuilder.AddOutbox();
        modelBuilder.AddInbox();
    }
}
