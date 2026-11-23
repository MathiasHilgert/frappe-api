namespace Frappe.Migrator.Settings;

/// <summary>A database participant's login role, bound from <c>Roles:&lt;name&gt;</c>.</summary>
public sealed class RoleOptions
{
    /// <summary>
    /// The password the login role is given. Unit: plain text, kept in configuration or a secret, never logged.
    /// Default: <see langword="null"/>, which <see cref="MigrationOptionsValidator"/> refuses at startup.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>Describes the options without the password, so a log line or a debugger never shows it.</summary>
    public override string ToString() => $"RoleOptions {{ Password = {(Password is null ? "<unset>" : "***")} }}";
}
