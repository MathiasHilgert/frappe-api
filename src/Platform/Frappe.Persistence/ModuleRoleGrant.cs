namespace Frappe.Persistence;

/// <summary>A module's login role: the key of its password under <c>Roles</c>, its role name and the schema it may read and write.</summary>
/// <param name="Name">The role's name under <c>Roles</c> in the configuration, such as <c>Orders</c>.</param>
/// <param name="Role">The Postgres login role.</param>
/// <param name="Schema">The schema the role gets data access to.</param>
public sealed record ModuleRoleGrant(string Name, string Role, string Schema);
