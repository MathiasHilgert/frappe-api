namespace Frappe.Platform.Modularity;

/// <summary>The rules across modules, checked from their metadata alone so they run before any service exists.</summary>
internal static class ModuleChecks
{
    // The platform's own tables and those of the libraries it hosts live in these schemas.
    private static readonly string[] _platformSchemas = ["platform", "wolverine", "quartz"];

    public static void ThrowIfInvalid(IReadOnlyList<IModule> modules)
    {
        ModuleMetadata[] metadata = [.. modules.Select(static module => module.Metadata)];
        List<string> violations =
        [
            .. Duplicates(metadata, static module => module.Name, "name"),
            .. Duplicates(metadata, static module => module.Schema, "schema"),
            .. Duplicates(metadata, static module => module.RoutePrefix, "route prefix"),
            .. metadata
                .Where(static module =>
                    _platformSchemas.Contains(module.Schema, StringComparer.Ordinal)
                )
                .Select(static module =>
                    $"Module '{module.Name}' uses schema '{module.Schema}', which the platform owns."
                ),
            .. OverlappingEventIds(metadata),
            .. UnpublishedSubscriptions(metadata),
        ];
        // TODO: the Wolverine bus PR adds the check that every event a module handles is in its SubscribedEvents.
        // TODO: module dependencies are checked here only if a real need appears; none is planned.
        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                $"The modules break {violations.Count} boot check(s):{Environment.NewLine}- "
                    + string.Join($"{Environment.NewLine}- ", violations)
            );
        }
    }

    private static IEnumerable<string> Duplicates(
        ModuleMetadata[] metadata,
        Func<ModuleMetadata, string> value,
        string what
    ) =>
        metadata
            .GroupBy(value, StringComparer.Ordinal)
            .Where(static group => group.Skip(1).Any())
            .Select(group =>
                $"Modules {string.Join(", ", group.Select(static module => $"'{module.Name}'"))} share {what} '{group.Key}'."
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
}
