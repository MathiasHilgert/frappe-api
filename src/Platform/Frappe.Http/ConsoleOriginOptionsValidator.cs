using Microsoft.Extensions.Options;

namespace Frappe.Http;

/// <summary>Fails at startup, naming the key and the entry, on a console origin that is not one (<see cref="ConsoleOriginOptions"/>).</summary>
internal sealed class ConsoleOriginOptionsValidator : IValidateOptions<ConsoleOriginOptions>
{
    public ValidateOptionsResult Validate(string? name, ConsoleOriginOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = ConsoleOriginOptions.Entries(options.Origins)
            .Where(entry => !ConsoleOriginOptions.TryNormalize(entry, out _))
            .Select(entry => $"Configuration '{ConsoleOriginOptions.Section}:{nameof(ConsoleOriginOptions.Origins)}' has an entry that is not an https origin "
                + $"(or http on localhost): '{entry}'.")
            .ToArray();
        if (failures.Length > 0)
        {
            return ValidateOptionsResult.Fail(failures);
        }

        // Parsed here, once, at startup: the requests the CORS policy and the Origin check answer only look it up.
        _ = options.Allowed();
        return ValidateOptionsResult.Success;
    }
}
