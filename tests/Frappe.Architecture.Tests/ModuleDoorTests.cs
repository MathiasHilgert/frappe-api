using System.Reflection;
using Frappe.Testing;

namespace Frappe.Architecture.Tests;

/// <summary>
/// Each module is entered through one door: the project <c>Frappe.&lt;M&gt;</c> and its <c>&lt;M&gt;Module</c>, with <c>Api</c> and <c>Worker</c>
/// (S1, ARCH-11). The Api and the Worker name nothing else from a module, and no door opens another module's door. The Migrator keeps
/// naming <c>&lt;M&gt;Database</c> from Infrastructure (<see cref="HostReferenceTests"/>). Checked on the compiled assemblies: the compiler
/// only records a reference to an assembly whose types the code names.
/// </summary>
public sealed class ModuleDoorTests
{
    /// <summary>Every module of the solution: one folder per module under <c>src/Modules</c>.</summary>
    private static readonly string[] AllModules =
    [
        .. new DirectoryInfo(Path.Combine(RepositoryRoot.FullPath, "src", "Modules")).GetDirectories()
            .Select(folder => folder.Name)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>The modules whose hosts still list their facets: Notifications moves behind its door in T3, which removes it here.</summary>
    private static readonly string[] NotYetBehindADoor = ["Notifications"];

    private static readonly string[] Doors = [.. AllModules.Except(NotYetBehindADoor).Select(module => $"Frappe.{module}")];

    private static Assembly Host(string host) => host switch
    {
        "Api" => typeof(Frappe.Api.ApiComposition).Assembly,
        _ => typeof(Frappe.Worker.WorkerComposition).Assembly,
    };

    /// <summary>What <paramref name="assembly"/> names from inside <paramref name="modules"/>: any project of theirs but the door.</summary>
    private static string[] PastTheDoor(Assembly assembly, IEnumerable<string> modules) =>
    [
        .. assembly.GetReferencedAssemblies().Select(reference => reference.Name!)
            .Where(name => modules.Any(module => name.StartsWith($"Frappe.{module}.", StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal),
    ];

    [Test]
    [Arguments("Api")]
    [Arguments("Worker")]
    public async Task A_host_names_only_the_door_of_each_module(string host)
    {
        // Arrange
        var assembly = Host(host);
        var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToArray();

        // Act
        var offenders = PastTheDoor(assembly, AllModules.Except(NotYetBehindADoor));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(offenders).IsEmpty().Because($"{host} names a module past its door: {string.Join(", ", offenders)}");
            await Assert.That(references).Contains("Frappe.Access").And.Contains("Frappe.Identity");
        }
    }

    [Test]
    public async Task The_rule_can_see_a_host_that_names_a_module_past_its_door()
    {
        // Act
        // Positive control: the Api still names Notifications' projects until T3, so the same check must find them.
        var seen = PastTheDoor(Host("Api"), ["Notifications"]);

        // Assert
        await Assert.That(seen).Contains("Frappe.Notifications.Http");
    }

    [Test]
    public async Task A_door_exposes_only_its_module_class()
    {
        // Act
        var exposed = Doors.SelectMany(door => System.Reflection.Assembly.Load(door).GetExportedTypes().Select(type => type.FullName!)).ToArray();

        // Assert
        await Assert.That(exposed).IsEquivalentTo(Doors.Select(door => $"{door}.{door["Frappe.".Length..]}Module"));
    }

    [Test]
    public async Task No_door_references_another_modules_door()
    {
        // Act
        var offenders = Doors
            .SelectMany(door => System.Reflection.Assembly.Load(door).GetReferencedAssemblies()
                .Select(reference => reference.Name!)
                .Where(name => AllModules
                    .Where(module => $"Frappe.{module}" != door)
                    .Any(module => name == $"Frappe.{module}" || name.StartsWith($"Frappe.{module}.", StringComparison.Ordinal)))
                .Select(name => $"{door} -> {name}"))
            .ToArray();

        // Assert
        await Assert.That(offenders).IsEmpty().Because(string.Join(", ", offenders));
    }
}
