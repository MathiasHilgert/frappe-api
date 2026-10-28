using Frappe.Platform.Modularity;
using Npgsql;
using Wolverine;

namespace Frappe.Platform.Messaging;

// Wolverine resolves IWolverineExtension services when it builds its options, after every module is
// registered, so the handlers come from the modules the host actually runs and nothing else.
internal sealed class ModuleHandlerDiscovery(
    IEnumerable<IModule> modules,
    NpgsqlDataSource dataSource
) : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        IModule[] registered = [.. modules];
        // The Kernel handler interfaces (commands, queries, integration-event consumers) a registered module
        // implements; ModuleChecks has already rejected a command or query without exactly one handler and an
        // event consumed but not subscribed.
        HashSet<Type> handlers = [.. registered.SelectMany(ModuleTypes.Handlers)];
        CheckConnectionBudget(handlers);
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
    // one holds a pooled connection for its transaction; checked here, before anything connects.
    private void CheckConnectionBudget(IEnumerable<Type> handlers)
    {
        int queues = handlers.Sum(static handler => ModuleTypes.HandlerInterfaces(handler).Count());
        int busy = queues * MessagingExtensions.LocalQueueParallelism;
        int pool = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString).MaxPoolSize;
        if (busy > pool)
        {
            throw new InvalidOperationException(
                $"The bus's {queues} local queues can run {busy} handlers at once "
                    + $"({MessagingExtensions.LocalQueueParallelism} each), more than the connection pool of {pool}, "
                    + "so handlers would wait on each other and starve every request. Raise Maximum Pool Size in "
                    + $"ConnectionStrings:Frappe above {busy}."
            );
        }
    }
}
