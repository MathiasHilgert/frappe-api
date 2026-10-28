using System.Reflection;
using Frappe.Kernel.Messaging;

namespace Frappe.Platform.Modularity;

// The types a module owns: the public types of its Infrastructure assembly (the IModule's) and of the
// Application assembly that assembly references, under the module's root namespace. A module class in
// Frappe.Order.Infrastructure owns Frappe.Order.*; the namespace keeps fixture modules that share one test
// assembly apart.
internal static class ModuleTypes
{
    private const string InfrastructureSegment = ".Infrastructure";

    private static readonly Type[] _handlerInterfaces =
    [
        typeof(ICommandHandler<,>),
        typeof(IQueryHandler<,>),
        typeof(IIntegrationEventConsumer<>),
    ];

    public static IReadOnlyList<Assembly> Assemblies(IModule module)
    {
        Assembly infrastructure = module.GetType().Assembly;
        string name = infrastructure.GetName().Name ?? "";
        string application = $"{name[..Math.Max(name.LastIndexOf('.'), 0)]}.Application";
        return
        [
            infrastructure,
            .. infrastructure
                .GetReferencedAssemblies()
                .Where(reference =>
                    string.Equals(reference.Name, application, StringComparison.Ordinal)
                )
                .Select(Assembly.Load),
        ];
    }

    public static IEnumerable<Type> Owned(IModule module)
    {
        string root = Root(module.GetType().Namespace ?? "");
        return Assemblies(module)
            .SelectMany(static assembly => assembly.GetExportedTypes())
            .Where(type =>
                type.Namespace is { } ns
                && (
                    string.Equals(ns, root, StringComparison.Ordinal)
                    || ns.StartsWith($"{root}.", StringComparison.Ordinal)
                )
            );
    }

    // Every handler the bus may call: concrete types closing a Kernel handler interface.
    public static IEnumerable<Type> Handlers(IModule module) =>
        Owned(module)
            .Where(static type =>
                type is { IsAbstract: false, IsGenericTypeDefinition: false }
                && HandlerInterfaces(type).Any()
            );

    // The closed Kernel handler interfaces a type implements.
    public static IEnumerable<Type> HandlerInterfaces(Type type) =>
        type.GetInterfaces()
            .Where(static contract =>
                contract.IsGenericType
                && _handlerInterfaces.Contains(contract.GetGenericTypeDefinition())
            );

    // Where the module's types are searched, for boot-check messages: "Frappe.Order.* in Frappe.Order.Infrastructure, ...".
    public static string Scope(IModule module) =>
        $"{Root(module.GetType().Namespace ?? "")}.* in {string.Join(", ", Assemblies(module).Select(static assembly => assembly.GetName().Name))}";

    private static string Root(string moduleNamespace) =>
        moduleNamespace.EndsWith(InfrastructureSegment, StringComparison.Ordinal)
            ? moduleNamespace[..^InfrastructureSegment.Length]
            : moduleNamespace;
}
