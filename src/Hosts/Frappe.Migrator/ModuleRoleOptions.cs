namespace Frappe.Migrator;

/// <summary>A module's login role, bound from <c>Roles:&lt;module&gt;</c>.</summary>
public sealed class ModuleRoleOptions
{
    /// <summary>The role's password.</summary>
    public string? Password { get; set; }
}
