using Frappe.Persistence;

namespace Frappe.Migrator;

/// <summary>
/// The one list of what the Migrator sets up. A module is added here by one row, never discovered by scanning
/// assemblies; an empty list still runs the whole sequence (lock, no roles, no migrations, revoke).
/// </summary>
internal static class Modules
{
    /// <summary>The databases migrated, in the order listed: each module's migrations run after the roles and before the grants.</summary>
    internal static readonly IDatabaseModule[] Databases = [];
}
