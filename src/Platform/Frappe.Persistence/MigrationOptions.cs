namespace Frappe.Persistence;

/// <summary>
/// What a database migration run needs: the owner's connection string (<c>ConnectionStrings:Owner</c>) and the password
/// of each login role (<c>Roles:&lt;name&gt;:Password</c>), validated by <see cref="MigrationOptionsValidator"/> before
/// the database is touched.
/// </summary>
public sealed class MigrationOptions
{
    /// <summary>The name of the owner's connection string, <c>ConnectionStrings:Owner</c>.</summary>
    public const string OwnerConnectionStringName = "Owner";

    /// <summary>The section of the login roles' passwords.</summary>
    public const string RolesSection = "Roles";

    /// <summary>
    /// The Npgsql connection string of the owner role, which creates the roles and runs the migrations. Unit: an Npgsql
    /// connection string. Default: <see langword="null"/>, which <see cref="MigrationOptionsValidator"/> refuses at startup.
    /// Nothing here binds configuration: the host maps <c>ConnectionStrings:Owner</c> (<see cref="OwnerConnectionStringName"/>)
    /// to this property.
    /// </summary>
    public string? OwnerConnectionString { get; set; }

    /// <summary>
    /// Each login role's settings, by its name under <see cref="RolesSection"/> (case-insensitive). Unit: one entry per
    /// role. Default: empty, which <see cref="MigrationOptionsValidator"/> refuses at startup until every role has a password.
    /// </summary>
    public Dictionary<string, ModuleRoleOptions> Roles { get; } = [with(StringComparer.OrdinalIgnoreCase)];
}
