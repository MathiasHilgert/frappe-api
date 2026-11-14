using System.Net;
using Microsoft.Extensions.Options;

namespace Frappe.Api;

/// <summary>Fails at startup, naming the key and the entry, on a proxy address or network that does not parse.</summary>
public sealed class TrustedProxyOptionsValidator : IValidateOptions<TrustedProxyOptions>
{
    public ValidateOptionsResult Validate(string? name, TrustedProxyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = Invalid(nameof(options.KnownProxies), options.KnownProxies, entry => IPAddress.TryParse(entry, out _))
            .Concat(Invalid(nameof(options.KnownNetworks), options.KnownNetworks, entry => IPNetwork.TryParse(entry, out _)))
            .ToArray();
        return failures.Length == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static IEnumerable<string> Invalid(string key, string? list, Func<string, bool> parses) =>
        TrustedProxyOptions.Entries(list)
            .Where(entry => !parses(entry))
            .Select(entry => $"Configuration '{TrustedProxyOptions.Section}:{key}' has an invalid entry '{entry}'.");
}
