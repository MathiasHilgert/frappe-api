using Frappe.Kernel.Domain;
using Frappe.Kernel.Messaging;

namespace Frappe.Platform.Modularity;

/// <summary>The rules across modules, checked from their metadata and their types so they run before any service exists.</summary>
internal static class ModuleChecks
{
    // The platform's own tables and those of the libraries it hosts live in these schemas, next to the ones
    // PostgreSQL owns: public, information_schema and every pg_* schema.
    private static readonly string[] _reservedSchemas =
    [
        "platform",
        "wolverine",
        "quartz",
        "public",
        "information_schema",
    ];

    public static void ThrowIfInvalid(IReadOnlyList<IModule> modules)
    {
        ModuleMetadata[] metadata = [.. modules.Select(static module => module.Metadata)];
        List<string> violations =
        [
            .. Duplicates(metadata, static module => [module.Name], "name"),
            .. Duplicates(metadata, static module => [module.Schema], "schema"),
            .. Duplicates(metadata, static module => [module.RoutePrefix], "route prefix"),
            .. Duplicates(metadata, static module => module.Permissions, "permission"),
            .. metadata
                .Where(static module => IsReserved(module.Schema))
                .Select(static module =>
                    $"Module '{module.Name}' uses schema '{module.Schema}', which the platform or PostgreSQL owns."
                ),
            .. OverlappingEventIds(metadata),
            .. SharedPublications(metadata),
            .. UnpublishedSubscriptions(metadata),
            .. UnsubscribedHandlers(modules),
            .. MessagesWithoutOneHandler(modules),
            .. MessagesWithoutIdentity(modules),
        ];
        // TODO: module dependencies are checked here only if a real need appears; none is planned.
        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                $"The modules break {violations.Count} boot check(s):{Environment.NewLine}- "
                    + string.Join($"{Environment.NewLine}- ", violations)
            );
        }
    }

    private static bool IsReserved(string schema) =>
        _reservedSchemas.Contains(schema, StringComparer.Ordinal)
        || schema.StartsWith("pg_", StringComparison.Ordinal);

    private static IEnumerable<string> Duplicates(
        ModuleMetadata[] metadata,
        Func<ModuleMetadata, IEnumerable<string>> values,
        string what
    ) =>
        metadata
            .SelectMany(module => values(module).Select(value => (Module: module, Value: value)))
            .GroupBy(static pair => pair.Value, StringComparer.Ordinal)
            .Where(static group => group.Skip(1).Any())
            .Select(group =>
                $"Modules {string.Join(", ", group.Select(static pair => $"'{pair.Module.Name}'"))} share {what} '{group.Key}'."
            );

    private static IEnumerable<string> SharedPublications(ModuleMetadata[] metadata) =>
        metadata
            .SelectMany(static module =>
                module.PublishedEvents.Select(published => (Module: module, Event: published))
            )
            .GroupBy(static pair => pair.Event)
            .Where(static group => group.Skip(1).Any())
            .Select(static group =>
                $"Modules {string.Join(", ", group.Select(static pair => $"'{pair.Module.Name}'"))} publish {group.Key.FullName}; an event belongs to one module."
            );

    private static IEnumerable<string> OverlappingEventIds(ModuleMetadata[] metadata) =>
        from (ModuleMetadata Module, int Index) first in metadata.Select(
            static (module, index) => (module, index)
        )
        from second in metadata.Skip(first.Index + 1)
        where first.Module.EventIds.Overlaps(second.EventIds)
        select $"Modules '{first.Module.Name}' ({first.Module.EventIds}) and '{second.Name}' ({second.EventIds}) share EventIds.";

    private static IEnumerable<string> UnpublishedSubscriptions(ModuleMetadata[] metadata)
    {
        HashSet<Type> published = [.. metadata.SelectMany(static module => module.PublishedEvents)];
        return metadata.SelectMany(module =>
            module
                .SubscribedEvents.Where(subscribed => !published.Contains(subscribed))
                .Select(subscribed =>
                    $"Module '{module.Name}' subscribes to {subscribed.FullName}, which no module publishes."
                )
        );
    }

    // A handled event missing from SubscribedEvents would run without the boot check ever seeing the subscription.
    private static IEnumerable<string> UnsubscribedHandlers(IReadOnlyList<IModule> modules) =>
        from module in modules
        from handler in ModuleTypes.Handlers(module)
        from contract in ModuleTypes.HandlerInterfaces(handler)
        where contract.GetGenericTypeDefinition() == typeof(IIntegrationEventConsumer<>)
        let handled = contract.GenericTypeArguments[0]
        where !module.Metadata.SubscribedEvents.Contains(handled)
        select $"Module '{module.Metadata.Name}' handles {handled.FullName} in {handler.FullName}, which is not in its SubscribedEvents; add it to SubscribedEvents in the module's metadata.";

    // The dispatcher invokes a command or query on exactly one handler; none or two is a wiring bug.
    private static IEnumerable<string> MessagesWithoutOneHandler(IReadOnlyList<IModule> modules)
    {
        ILookup<Type, Type> handlers = (
            from handler in modules.SelectMany(ModuleTypes.Handlers).Distinct()
            from contract in ModuleTypes.HandlerInterfaces(handler)
            where contract.GetGenericTypeDefinition() != typeof(IIntegrationEventConsumer<>)
            select (Message: contract.GenericTypeArguments[0], Handler: handler)
        ).ToLookup(static pair => pair.Message, static pair => pair.Handler);
        return from module in modules
            from message in ModuleTypes.Owned(module)
            where !message.IsAbstract && IsCommandOrQuery(message)
            let found = handlers[message].ToArray()
            where found.Length != 1
            select found.Length == 0
                ? $"{message.FullName} has 0 handlers; a command or query has exactly one. For module '{module.Metadata.Name}' the bus searched the public types under {ModuleTypes.Scope(module)}; add a public class there implementing the Kernel handler interface for it."
                : $"{message.FullName} has {found.Length} handlers ({string.Join(", ", found.Select(static handler => handler.FullName))}); a command or query has exactly one, so remove all but one.";
    }

    // Durable queues, schedules and dead letters store a message under its identity, so one without it would
    // be stored under its class name and stranded by a rename.
    private static IEnumerable<string> MessagesWithoutIdentity(IReadOnlyList<IModule> modules) =>
        from module in modules
        from message in ModuleTypes.Owned(module)
        where
            message is { IsAbstract: false, IsInterface: false }
            && IsPersisted(message)
            && !Attribute.IsDefined(message, typeof(MessageIdentityAttribute))
        select $"{message.FullName} is stored by the bus but has no [MessageIdentity]; add [MessageIdentity(\"{module.Metadata.Name}.<name>\")] with a name that never changes.";

    private static bool IsPersisted(Type type) =>
        type.IsAssignableTo(typeof(IIntegrationEvent))
        || type.IsAssignableTo(typeof(IDomainEvent))
        || type.GetInterfaces()
            .Any(static contract =>
                contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(ICommand<,>)
            );

    private static bool IsCommandOrQuery(Type type) =>
        type.GetInterfaces()
            .Any(static contract =>
                contract.IsGenericType
                && (
                    contract.GetGenericTypeDefinition() == typeof(ICommand<,>)
                    || contract.GetGenericTypeDefinition() == typeof(IQuery<,>)
                )
            );
}
