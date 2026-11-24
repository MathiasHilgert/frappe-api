using Microsoft.Extensions.Options;

namespace Frappe.Identity.Infrastructure.Sessions;

/// <summary>
/// Fails at startup, naming the full key, when a session lifetime is not positive or the idle one outlasts the absolute one (CFG-03).
/// </summary>
public sealed class SessionOptionsValidator : IValidateOptions<SessionOptions>
{
    private const string Idle = $"{SessionOptions.Section}:IdleTimeout";

    private const string Absolute = $"{SessionOptions.Section}:AbsoluteTimeout";

    public ValidateOptionsResult Validate(string? name, SessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        List<string> failures = [];
        if (options.IdleTimeout <= TimeSpan.Zero)
        {
            failures.Add($"Configuration '{Idle}' must be positive.");
        }

        if (options.AbsoluteTimeout <= TimeSpan.Zero)
        {
            failures.Add($"Configuration '{Absolute}' must be positive.");
        }

        if (options.IdleTimeout > options.AbsoluteTimeout)
        {
            failures.Add($"Configuration '{Idle}' must not be longer than '{Absolute}'.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
