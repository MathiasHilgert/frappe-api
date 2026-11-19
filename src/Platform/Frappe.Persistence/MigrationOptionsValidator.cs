using Microsoft.Extensions.Options;

namespace Frappe.Persistence;

/// <summary>Fails, naming each missing key, when the owner's connection string or a login role's password is missing.</summary>
/// <param name="roles">The names, under <c>Roles</c>, of the login roles that must have a password.</param>
public sealed class MigrationOptionsValidator(IReadOnlyCollection<string> roles) : IValidateOptions<MigrationOptions>
{
    public ValidateOptionsResult Validate(string? name, MigrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.OwnerConnectionString))
        {
            failures.Add($"Configuration 'ConnectionStrings:{MigrationOptions.OwnerConnectionStringName}' is required to migrate the database.");
        }

        foreach (var role in roles)
        {
            if (!options.Roles.TryGetValue(role, out var settings) || string.IsNullOrWhiteSpace(settings.Password))
            {
                failures.Add($"Configuration '{MigrationOptions.RolesSection}:{role}:{nameof(ModuleRoleOptions.Password)}' is required to migrate the database.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
