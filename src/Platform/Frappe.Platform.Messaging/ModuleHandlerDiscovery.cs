using Frappe.Platform.Hosting;
using Frappe.Platform.Modularity;
using Microsoft.Extensions.Options;
using Npgsql;
using Wolverine;

namespace Frappe.Platform.Messaging;

// Wolverine resolves IWolverineExtension services when it builds its options, after every module is
// registered, so the handlers come from the modules the host actually runs and nothing else.
internal sealed class ModuleHandlerDiscovery(
    IEnumerable<IModule> modules,
    NpgsqlDataSource dataSource,
    IOptions<PlatformOptions> platform
) : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        IModule[] registered = [.. modules];
        // The Kernel handler interfaces (commands, queries, integration-event consumers) a registered module
        // implements; ModuleChecks has already rejected a command or query without exactly one handler and an
        // event consumed but not subscribed.
        HashSet<Type> handlers = [.. registered.SelectMany(ModuleTypes.Handlers)];
        int listeners = NatsEvents.Listens(platform.Value.Role)
            ? NatsEvents.ConsumedEvents(registered).Count()
            : 0;
        CheckConnectionBudget(handlers, listeners);
        foreach (IModule module in registered)
        {
            foreach (System.Reflection.Assembly assembly in ModuleTypes.Assemblies(module))
            {
                options.Discovery.IncludeAssembly(assembly);
            }
        }

        options.Discovery.CustomizeHandlerDiscovery(query =>
            query.Includes.WithCondition(
                "Implements a Kernel handler interface in a registered module",
                handlers.Contains
            )
        );
    }

    // Each message a handler takes gets its own local queue (separated mode), and each handler running on
    // one holds a pooled connection for its transaction; so does a NATS listener, which runs the handler itself
    // when one module subscribes. Checked here, before anything connects.
    private void CheckConnectionBudget(IEnumerable<Type> handlers, int listeners)
    {
        int queues = handlers.Sum(static handler => ModuleTypes.HandlerInterfaces(handler).Count());
        int busy =
            (queues * MessagingExtensions.LocalQueueParallelism)
            + (listeners * NatsTopology.ListenerParallelism);
        int pool = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString).MaxPoolSize;
        if (busy > pool)
        {
            throw new InvalidOperationException(
                $"The bus's {queues} local queues ({MessagingExtensions.LocalQueueParallelism} each) and "
                    + $"{listeners} NATS listeners ({NatsTopology.ListenerParallelism} each) can run {busy} "
                    + $"handlers at once, more than the connection pool of {pool}, "
                    + "so handlers would wait on each other and starve every request. Raise Maximum Pool Size in "
                    + $"ConnectionStrings:Frappe above {busy}."
            );
        }
    }
}
