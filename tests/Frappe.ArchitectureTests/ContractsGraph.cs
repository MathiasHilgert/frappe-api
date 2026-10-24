namespace Frappe.ArchitectureTests;

/// <summary>The module dependency graph read from project references to other modules' Contracts.</summary>
internal static class ContractsGraph
{
    /// <summary>One cycle as "A -> B -> A", or null when the graph is acyclic.</summary>
    public static string? FindCycle(IEnumerable<Project> projects)
    {
        var all = projects.ToList();
        var byName = all.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var edges = all.Where(p => p.Module is not null)
            .SelectMany(p => p.References
                .Select(r => byName.GetValueOrDefault(r))
                .OfType<Project>()
                .Where(r => r.Kind == ProjectKind.ModuleContracts && r.Module != p.Module)
                .Select(r => (From: p.Module!, To: r.Module!)))
            .Distinct()
            .ToLookup(e => e.From, e => e.To, StringComparer.Ordinal);

        var done = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in all.Select(p => p.Module).OfType<string>().Distinct(StringComparer.Ordinal))
        {
            if (Visit(module, edges, done, []) is { } cycle)
                return cycle;
        }

        return null;
    }

    private static string? Visit(string module, ILookup<string, string> edges, HashSet<string> done, List<string> path)
    {
        if (path.Contains(module, StringComparer.Ordinal))
            return string.Join(" -> ", path.SkipWhile(m => m != module).Append(module));
        if (!done.Add(module))
            return null;

        path.Add(module);
        foreach (var next in edges[module])
        {
            if (Visit(next, edges, done, path) is { } cycle)
                return cycle;
        }

        path.RemoveAt(path.Count - 1);
        return null;
    }
}
