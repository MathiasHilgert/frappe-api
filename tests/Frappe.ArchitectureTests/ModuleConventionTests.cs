using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Frappe.Modularity;
using Frappe.Platform.Persistence;
using Mono.Cecil;
using Shouldly;

namespace Frappe.ArchitectureTests;

/// <summary>Module-level rules the README documents: how the host, Contracts, folders and schemas are shaped.</summary>
public sealed partial class ModuleConventionTests
{
    public static TheoryData<string> ContractsProjects => [.. Solution.OfKind(ProjectKind.ModuleContracts).Select(p => p.Name)];

    public static TheoryData<string> ModuleProjects => [.. Solution.Projects.Where(p => p.Module is not null).Select(p => p.Name)];

    public static TheoryData<string> ApplicationProjects => [.. Solution.OfKind(ProjectKind.ModuleApplication).Select(p => p.Name)];

    public static TheoryData<string> InfrastructureProjects => [.. Solution.OfKind(ProjectKind.ModuleInfrastructure).Select(p => p.Name)];

    [Fact]
    public void Host_KnowsEachModuleOnlyThroughItsModuleType()
    {
        var host = Solution.OfKind(ProjectKind.Host).Single();
        var moduleNamespaces = Solution.ModuleNames.Select(m => $"Frappe.{m}.").ToArray();
        using var definition = ModuleDefinition.ReadModule(Solution.LoadAssembly(host).Location);

        // Generated Wolverine code is the composition's output, not hand-written host code.
        var offenders = definition.GetTypes()
            .Where(t => !Outermost(t).Namespace.StartsWith("Internal.Generated", StringComparison.Ordinal))
            .SelectMany(t => ReferencedTypes(t).Select(r => (Host: t, Reference: r)))
            .Where(x => moduleNamespaces.Any(ns => x.Reference.Namespace.StartsWith(ns, StringComparison.Ordinal)))
            .Where(x => !IsModuleType(x.Reference))
            .Select(x => $"{x.Host.FullName} -> {x.Reference.FullName}")
            .Distinct(StringComparer.Ordinal);

        offenders.ShouldBeEmpty("The host composes modules through IModule; meters, endpoints and options come through it too.");
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContractsProjects))]
    public void Contracts_HoldOnlyMessages(string name)
    {
        var offenders = Solution.LoadAssembly(Solution.Projects.Single(p => p.Name == name)).GetTypes()
            // Coverage instrumentation injects tracker types into every assembly; only the project's own types count.
            .Where(t => !t.IsNested && !t.IsDefined(typeof(CompilerGeneratedAttribute))
                        && t.Namespace?.StartsWith("Frappe.", StringComparison.Ordinal) == true)
            // Cross-module queries: an interface and the records it answers with, in the Contracts Queries folder.
            .Where(t => !t.Namespace!.EndsWith(".Queries", StringComparison.Ordinal))
            .Where(t => !IsMessage(t) && !t.IsEnum && !IsConstantsHolder(t) && !t.Name.EndsWith("ContractsMarker", StringComparison.Ordinal))
            .Select(t => t.FullName);

        offenders.ShouldBeEmpty("Contracts carry messages (records), enums and constants, plus query interfaces in Queries; services belong to a platform abstraction.");
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ModuleProjects))]
    public void SourceFile_NamespaceIsTheProjectPlusItsFeatureFolder(string name)
    {
        var project = ProjectDirectory(name);
        var offenders = SourceFiles(project)
            .Select(f => (File: f, Folders: Path.GetRelativePath(project, Path.GetDirectoryName(f)!).Split(Path.DirectorySeparatorChar)
                .Where(s => s != ".").ToArray()))
            .Where(x => !x.Folders.Contains("Migrations"))
            .Select(x => (x.File, Expected: x.Folders.Length == 0 ? name : $"{name}.{x.Folders[0]}", Actual: NamespaceOf(x.File)))
            .Where(x => x.Actual != x.Expected)
            .Select(x => $"{Path.GetRelativePath(project, x.File)}: {x.Actual} (expected {x.Expected})");

        offenders.ShouldBeEmpty("A namespace names the feature area (first folder); a use-case folder below it stays out of the namespace.");
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ApplicationProjects))]
    public void UseCaseFolder_HoldsItsMessageAndItsHandler(string name)
    {
        var project = ProjectDirectory(name);
        var offenders = Directory.EnumerateDirectories(project)
            .Where(d => !IsBuildOutput(d))
            .SelectMany(Directory.EnumerateDirectories)
            .Where(d => !File.Exists(Path.Join(d, $"{Path.GetFileName(d)}.cs"))
                        || !File.Exists(Path.Join(d, $"{Path.GetFileName(d)}Handler.cs")))
            .Select(d => Path.GetRelativePath(project, d));

        offenders.ShouldBeEmpty("A use-case folder <Area>/<UseCase>/ holds <UseCase>.cs and <UseCase>Handler.cs.");
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(InfrastructureProjects))]
    public void Module_OwnsAtMostOneDbContext_ThePlatformsSchemaPerModuleContext(string name)
    {
        var contexts = Solution.LoadAssembly(Solution.Projects.Single(p => p.Name == name)).GetTypes()
            .Where(t => typeof(Microsoft.EntityFrameworkCore.DbContext).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        contexts.Count.ShouldBeLessThanOrEqualTo(1);
        contexts.ShouldAllBe(c => typeof(ModuleDbContext).IsAssignableFrom(c));
    }

    private static TypeDefinition Outermost(TypeDefinition type)
    {
        while (type.DeclaringType is { } declaring)
            type = declaring;
        return type;
    }

    private static bool IsModuleType(TypeReference reference)
    {
        var type = Type.GetType($"{reference.FullName.Replace('/', '+')}, {reference.Scope.Name.Replace(".dll", string.Empty, StringComparison.Ordinal)}");
        return type is not null && typeof(IModule).IsAssignableFrom(type);
    }

    private static IEnumerable<TypeReference> ReferencedTypes(TypeDefinition type) =>
        type.Methods.Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions.Select(i => i.Operand))
            .SelectMany(operand => operand switch
            {
                MethodReference method => [method.DeclaringType],
                FieldReference field => [field.DeclaringType, field.FieldType],
                TypeReference typeReference => new[] { typeReference },
                _ => [],
            })
            .Concat(type.Fields.Select(f => f.FieldType))
            .Concat(type.Properties.Select(p => p.PropertyType))
            .Where(r => r is not null && r is not GenericParameter);

    private static bool IsMessage(Type type) => type.GetMethod("<Clone>$") is not null;

    private static bool IsConstantsHolder(Type type) =>
        type is { IsAbstract: true, IsSealed: true }
        && type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .All(m => m is FieldInfo { IsLiteral: true });

    private static string ProjectDirectory(string name)
    {
        var project = Solution.Projects.Single(p => p.Name == name);
        return Path.Join(Solution.Root, "src", "Modules", project.Module!, name);
    }

    private static IEnumerable<string> SourceFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Where(f => !IsBuildOutput(f));

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || Path.GetFileName(path) is "obj" or "bin";

    private static string? NamespaceOf(string file) => FileScopedNamespace().Match(File.ReadAllText(file)) is { Success: true } m ? m.Groups["name"].Value : null;

    [GeneratedRegex(@"^namespace (?<name>[\w.]+);", RegexOptions.Multiline | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1_000)]
    private static partial Regex FileScopedNamespace();
}
