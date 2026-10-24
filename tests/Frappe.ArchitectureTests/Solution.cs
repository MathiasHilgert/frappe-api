using System.Reflection;
using System.Xml.Linq;

namespace Frappe.ArchitectureTests;

public enum ProjectKind
{
    SharedKernel,
    ContractsAbstractions,
    ApplicationAbstractions,
    Modularity,
    Platform,
    ModuleDomain,
    ModuleApplication,
    ModuleContracts,
    ModuleInfrastructure,
    Host,
}

/// <summary>A production project, classified purely by naming convention so new modules are covered automatically.</summary>
public sealed record Project(string Name, ProjectKind Kind, string? Module, IReadOnlyList<string> References)
{
    public override string ToString() => Name;
}

internal static class Solution
{
    private static readonly Lazy<IReadOnlyList<Project>> LazyProjects = new(LoadProjects);

    public static IReadOnlyList<Project> Projects => LazyProjects.Value;

    /// <summary>The repository root (where Frappe.slnx lives).</summary>
    public static string Root { get; } = FindRoot();

    public static IEnumerable<Project> OfKind(params ProjectKind[] kinds) => Projects.Where(p => kinds.Contains(p.Kind));

    public static IEnumerable<string> ModuleNames => Projects.Select(p => p.Module).OfType<string>().Distinct(StringComparer.Ordinal);

    public static Assembly LoadAssembly(Project project) =>
        Assembly.LoadFrom(Path.Join(AppContext.BaseDirectory, $"{project.Name}.dll"));

    private static List<Project> LoadProjects()
    {
        var src = Path.Join(Root, "src");
        return Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories)
            .Select(path =>
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var references = XDocument.Load(path).Descendants("ProjectReference")
                    .Select(r => Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value.Replace('\\', '/')))
                    .ToList();
                var (kind, module) = Classify(name);
                return new Project(name, kind, module, references);
            })
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static (ProjectKind Kind, string? Module) Classify(string name)
    {
        switch (name)
        {
            case "Frappe.SharedKernel": return (ProjectKind.SharedKernel, null);
            case "Frappe.Contracts.Abstractions": return (ProjectKind.ContractsAbstractions, null);
            case "Frappe.Application.Abstractions": return (ProjectKind.ApplicationAbstractions, null);
            case "Frappe.Modularity": return (ProjectKind.Modularity, null);
            case "Frappe.Api": return (ProjectKind.Host, null);
        }

        if (name.StartsWith("Frappe.Platform.", StringComparison.Ordinal))
            return (ProjectKind.Platform, null);

        var parts = name.Split('.');
        if (parts is ["Frappe", var module, var layer])
        {
            ProjectKind? kind = layer switch
            {
                "Domain" => ProjectKind.ModuleDomain,
                "Application" => ProjectKind.ModuleApplication,
                "Contracts" => ProjectKind.ModuleContracts,
                "Infrastructure" => ProjectKind.ModuleInfrastructure,
                _ => null,
            };
            if (kind is { } k)
                return (k, module);
        }

        throw new InvalidOperationException(
            $"Project '{name}' does not follow a known convention; classify it in {nameof(Solution)} before adding it.");
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Join(dir.FullName, "Frappe.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("Frappe.slnx not found above the test output directory.");
    }
}
