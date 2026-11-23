using System.ComponentModel.DataAnnotations;

namespace Frappe.Persistence;

/// <summary>
/// Where one module's database lives: <see cref="ConnectionString"/> from <c>ConnectionStrings:&lt;Module&gt;</c>, with the
/// module's own Postgres role. Registered as named options, one name per module (<see cref="ModuleDatabaseRegistration"/>),
/// and checked at startup by <see cref="ModuleDatabaseOptionsValidator"/>.
/// </summary>
public sealed class ModuleDatabaseOptions : IValidatableObject
{
    /// <summary>The module the options belong to, as <c>AddModuleDatabase</c> received it.</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>
    /// <c>ConnectionStrings:&lt;Module&gt;</c>: the connection string of the module's database, with the module's own role
    /// (read from the connection strings section). Required, no default; a missing value fails at startup. Changing it points
    /// the module at another database; its connection limits come from <c>Database:*</c>.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>The name of a module's connection string: the module's name with a capital, <c>orders</c> reads <c>Orders</c>.</summary>
    public static string ConnectionStringName(string module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        return char.ToUpperInvariant(module[0]) + module[1..];
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            yield return new ValidationResult(
                $"Configuration 'ConnectionStrings:{ConnectionStringName(Module)}' is required.",
                [nameof(ConnectionString)]);
        }
    }
}
