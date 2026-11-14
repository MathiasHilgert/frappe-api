namespace Frappe.Migrator.Settings;

/// <summary>
/// What the Migrator needs: the owner's connection string (<c>ConnectionStrings:Owner</c>) and the password of each
/// module's login role (<c>Roles:&lt;module&gt;:Password</c>), validated by <see cref="MigrationOptionsValidator"/>
/// before the database is touched.
/// </summary>
public sealed class MigrationOptions
{
    /// <summary>The name of the owner's connection string, <c>ConnectionStrings:Owner</c>.</summary>
    public const string OwnerConnectionStringName = "Owner";

    /// <summary>The section of the module roles' passwords.</summary>
    public const string RolesSection = "Roles";

    /// <summary>The connection string of the owner role, which runs the migrations.</summary>
    public string? OwnerConnectionString { get; set; }

    /// <summary>Each module role's settings, by the name it has under <see cref="RolesSection"/>.</summary>
    public Dictionary<string, ModuleRoleOptions> Roles { get; } = new(StringComparer.OrdinalIgnoreCase);
}
