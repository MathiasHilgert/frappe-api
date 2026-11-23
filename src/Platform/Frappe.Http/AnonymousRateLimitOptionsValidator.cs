using Microsoft.Extensions.Options;

namespace Frappe.Http;

/// <summary>
/// Fails at startup, naming the full key of every rate limit setting that cannot work: fewer than one permit, or a
/// window that is not positive (<see cref="AnonymousRateLimitOptions"/>).
/// </summary>
/// <param name="optionsName">The named options this validator checks: the policy's, or <see cref="Options.DefaultName"/> for the default
/// ones.</param>
/// <param name="section">The configuration section they were bound from.</param>
public sealed class AnonymousRateLimitOptionsValidator(string optionsName, string section) : IValidateOptions<AnonymousRateLimitOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AnonymousRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (name != optionsName)
        {
            return ValidateOptionsResult.Skip;
        }

        var failures = new List<string>();
        if (options.PermitLimit < 1)
        {
            failures.Add($"Configuration '{section}:{nameof(options.PermitLimit)}' must be at least 1.");
        }

        if (options.Window <= TimeSpan.Zero)
        {
            failures.Add($"Configuration '{section}:{nameof(options.Window)}' must be positive.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
