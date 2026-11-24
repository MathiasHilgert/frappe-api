using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Notifications.Infrastructure.Persistence;
using Frappe.Persistence;

namespace Frappe.Migrator;

/// <summary>
/// The modules whose databases the Migrator sets up, listed explicitly in one place: modules are never discovered by scanning.
/// The platform's own participants (the scheduler's job store) are added by <see cref="DatabaseMigration"/>, never listed here.
/// </summary>
public static class Modules
{
    /// <summary>Every module database the Migrator sets up, in order.</summary>
    public static IReadOnlyCollection<IDatabaseModule> All { get; } = [new IdentityDatabase(), new NotificationsDatabase()];
}
