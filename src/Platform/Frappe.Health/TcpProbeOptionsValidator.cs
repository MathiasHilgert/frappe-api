using Microsoft.Extensions.Options;

namespace Frappe.Health;

/// <summary>
/// Fails at startup, naming the full key of every probe port outside 1 to 65535 and of every two probes that share a
/// port (<see cref="TcpProbeOptions"/>): one port can only be open or closed, so two probes on it would answer as one.
/// </summary>
public sealed class TcpProbeOptionsValidator : IValidateOptions<TcpProbeOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TcpProbeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        (string Key, int Port)[] probes =
        [
            (nameof(options.LivePort), options.LivePort),
            (nameof(options.ReadyPort), options.ReadyPort),
            (nameof(options.StartupPort), options.StartupPort),
        ];

        var failures = new List<string>();
        foreach (var (key, port) in probes)
        {
            if (port is < 1 or > 65535)
            {
                failures.Add($"Configuration '{TcpProbeOptions.Section}:{key}' must be a port from 1 to 65535, not '{port}'.");
            }
        }

        for (var first = 0; first < probes.Length; first++)
        {
            for (var second = first + 1; second < probes.Length; second++)
            {
                if (probes[first].Port == probes[second].Port)
                {
                    failures.Add(
                        $"Configuration '{TcpProbeOptions.Section}:{probes[first].Key}' and '{TcpProbeOptions.Section}:{probes[second].Key}' "
                        + $"must be different ports, not both '{probes[first].Port}'.");
                }
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
