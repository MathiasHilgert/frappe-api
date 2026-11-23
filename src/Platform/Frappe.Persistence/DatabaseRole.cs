namespace Frappe.Persistence;

/// <summary>A Postgres login role a database participant needs, with where its password comes from and what it may do.</summary>
/// <param name="Name">The Postgres role, a plain lower-case identifier such as <c>frappe_orders</c>.</param>
/// <param name="ConfigName">The role's name in the Migrator's configuration, under <c>Roles</c>, where its password is read.</param>
/// <param name="Grant">What the role may do after the participant migrated.</param>
public sealed record DatabaseRole(string Name, string ConfigName, RoleGrant Grant);
