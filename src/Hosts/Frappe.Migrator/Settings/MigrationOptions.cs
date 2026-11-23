using Npgsql;

namespace Frappe.Migrator.Settings;

/// <summary>
/// What the Migrator needs: the owner's connection string (<c>ConnectionStrings:Owner</c>) and the password of each
/// database role's login (<c>Roles:&lt;name&gt;:Password</c>), validated by <see cref="MigrationOptionsValidator"/>
/// before the database is touched.
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
    /// </summary>
    public string? OwnerConnectionString { get; set; }

    /// <summary>
    /// Each login role's settings, by the role's config name under <see cref="RolesSection"/> (case-insensitive). Unit: one
    /// entry per role. Default: empty, which <see cref="MigrationOptionsValidator"/> refuses at startup until every login role has a password.
    /// </summary>
    public Dictionary<string, RoleOptions> Roles { get; } = [with(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Describes the options with the owner's connection string without its password and the role names only.</summary>
    public override string ToString()
    {
        var owner = "<unset>";
        if (OwnerConnectionString is not null)
        {
            try
            {
                var builder = new NpgsqlConnectionStringBuilder(OwnerConnectionString);
                builder.Remove("Password");
                owner = builder.ConnectionString;
            }
            catch (ArgumentException)
            {
                owner = "***";
            }
        }

        return $"MigrationOptions {{ OwnerConnectionString = {owner}, Roles = [{string.Join(", ", Roles.Keys)}] }}";
    }
}
