using Microsoft.Extensions.Options;

namespace Frappe.Caching;

/// <summary>Fails at startup, naming the full key, when the Valkey connection string is missing (<see cref="CachingOptions"/>).</summary>
public sealed class CachingOptionsValidator : IValidateOptions<CachingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, CachingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return string.IsNullOrWhiteSpace(options.ConnectionString)
            ? ValidateOptionsResult.Fail($"Configuration 'ConnectionStrings:{CachingOptions.ConnectionStringName}' is required.")
            : ValidateOptionsResult.Success;
    }
}
