using Frappe.Platform.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Wolverine.Runtime;

namespace Frappe.Platform.Messaging;

// A publishing role continues past failed resource checks so that it starts without NATS (NatsEvents), and that
// setting also lets it start on a missing or outdated message store. This runs Wolverine's own store check again
// and fails the start, as the listening roles do. Registered after AddWolverine, so it runs once the bus has started.
internal sealed class MessageStoreCheck(
    IWolverineRuntime runtime,
    IOptions<PlatformOptions> platform
) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        NatsEvents.Listens(platform.Value.Role)
            ? Task.CompletedTask
            : runtime.Stores.AssertStorageProvisionedAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
