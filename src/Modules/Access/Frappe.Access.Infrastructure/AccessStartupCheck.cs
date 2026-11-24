using Frappe.Access.Application;
using Microsoft.Extensions.Hosting;

namespace Frappe.Access.Infrastructure;

/// <summary>
/// Validates the final container when the host starts (S7): a request with zero or two markers, or a handler chain without the use-case gate,
/// stops the host there, naming every one, and not at the first request that slips through.
/// </summary>
internal sealed class AccessStartupCheck(IServiceProvider provider, IReadOnlyCollection<Type> ports) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var findings = UseCaseCheck.Findings(provider, ports);
        return findings.Count == 0
            ? Task.CompletedTask
            : throw new InvalidOperationException(
                "Access refuses to start:" + string.Concat(findings.Select(finding => $"{Environment.NewLine}- {finding}")));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
