namespace Frappe.Persistence;

/// <summary>A module's login role, bound from <c>Roles:&lt;module&gt;</c>.</summary>
public sealed class ModuleRoleOptions
{
    /// <summary>
    /// The password the module's login role is given. Unit: plain text, kept in configuration or a secret, never logged.
    /// Default: <see langword="null"/>, which <see cref="MigrationOptionsValidator"/> refuses at startup.
    /// </summary>
    public string? Password { get; set; }
}
