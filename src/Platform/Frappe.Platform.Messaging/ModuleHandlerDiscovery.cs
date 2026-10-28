using Frappe.Platform.Modularity;
using Wolverine;

namespace Frappe.Platform.Messaging;

// Wolverine resolves IWolverineExtension services when it builds its options, after every module is
// registered, so the handlers come from the modules the host actually runs and nothing else.
internal sealed class ModuleHandlerDiscovery(IEnumerable<IModule> modules) : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        IModule[] registered = [.. modules];
        // The Kernel handler interfaces (commands, queries, integration-event consumers) a registered module
        // implements; ModuleChecks has already rejected a command or query without exactly one handler and an
        // event consumed but not subscribed.
        HashSet<Type> handlers = [.. registered.SelectMany(ModuleTypes.Handlers)];
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
}
