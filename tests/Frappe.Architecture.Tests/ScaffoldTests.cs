using System.Diagnostics;
using Frappe.Testing;

namespace Frappe.Architecture.Tests;

/// <summary>
/// <c>just scaffold</c> writes skeletons that compile under the repository's analyzers. The script runs against a copy of the repository
/// (<c>SCAFFOLD_ROOT</c>), never the checkout, and the copy's projects are built there.
/// </summary>
public sealed class ScaffoldTests
{
    private const string Module = "Notifications";

    private static readonly string[] CrudFiles =
    [
        "src/Modules/Notifications/Frappe.Notifications.Domain/Widgets/Widget.cs",
        "src/Modules/Notifications/Frappe.Notifications.Infrastructure/Widgets/WidgetConfiguration.cs",
        "src/Modules/Notifications/Frappe.Notifications.Application/Widgets/IWidgets.cs",
        "src/Modules/Notifications/Frappe.Notifications.Application/Widgets/IWidgetReads.cs",
        "src/Modules/Notifications/Frappe.Notifications.Application/Widgets/WidgetView.cs",
        "src/Modules/Notifications/Frappe.Notifications.Infrastructure/Widgets/EfWidgets.cs",
        "src/Modules/Notifications/Frappe.Notifications.Infrastructure/Widgets/EfWidgetReads.cs",
        "src/Modules/Notifications/Frappe.Notifications.Application/Widgets/CreateWidget/CreateWidgetHandler.cs",
        "src/Modules/Notifications/Frappe.Notifications.Application/Widgets/GetWidget/GetWidgetHandler.cs",
        "src/Modules/Notifications/Frappe.Notifications.Application/Widgets/ListWidgets/ListWidgetsHandler.cs",
        "src/Modules/Notifications/Frappe.Notifications.Application/Widgets/UpdateWidget/UpdateWidgetHandler.cs",
        "src/Modules/Notifications/Frappe.Notifications.Application/Widgets/DeleteWidget/DeleteWidgetHandler.cs",
        "src/Modules/Notifications/Frappe.Notifications.Http/Widgets/CreateWidgetEndpoints.cs",
        "src/Modules/Notifications/Frappe.Notifications.Http/Widgets/DeleteWidgetEndpoints.cs",
        "tests/Modules/Notifications/Frappe.Notifications.UnitTests/Widgets/ListWidgetsTests.cs",
        "tools/bruno/notifications/create-a-widget.yml",
        "tools/bruno/notifications/delete-a-widget.yml",
    ];

    private static readonly string[] BuiltProjects =
    [
        "src/Modules/Notifications/Frappe.Notifications.Infrastructure",
        "src/Modules/Notifications/Frappe.Notifications.Http",
        "tests/Modules/Notifications/Frappe.Notifications.UnitTests",
    ];

    [Test]
    public async Task A_scaffolded_crud_builds_and_names_what_is_left_to_do()
    {
        // Arrange
        var copy = Directory.CreateTempSubdirectory("frappe-scaffold-");
        try
        {
            await CopyRepositoryAsync(copy.FullName);

            // Act
            var scaffold = await RunAsync(
                copy.FullName, "bash", Path.Combine(RepositoryRoot.FullPath, "eng", "scaffold", "scaffold.sh"), "crud", Module, "Widget");
            var builds = new List<(string Project, Run Run)>();
            if (scaffold.ExitCode == 0)
            {
                foreach (var project in BuiltProjects)
                {
                    builds.Add((project, await RunAsync(copy.FullName, "dotnet", "build", project, "-nologo", "-nodeReuse:false", "-clp:ErrorsOnly")));
                }
            }

            // Assert
            await Assert.That(scaffold.ExitCode).IsEqualTo(0).Because(scaffold.Output);
            using (Assert.Multiple())
            {
                foreach (var file in CrudFiles)
                {
                    await Assert.That(File.Exists(Path.Combine(copy.FullName, file))).IsTrue().Because($"crud writes {file}");
                }

                await Assert.That(scaffold.Output).Contains("dotnet ef migrations add AddWidgets");
                await Assert.That(scaffold.Output).Contains("just contracts update");
                await Assert.That(scaffold.Output).Contains("TryAddScoped<IWidgets, EfWidgets>");
                await Assert.That(scaffold.Output).Contains("MapCreateWidgetEndpoints");
                foreach (var (project, build) in builds)
                {
                    await Assert.That(build.ExitCode).IsEqualTo(0).Because($"{project} does not build:\n{build.Output}");
                }
            }
        }
        finally
        {
            copy.Delete(recursive: true);
        }
    }

    /// <summary>Copies what git tracks or would track (new files included, ignored output such as bin and obj left out).</summary>
    private static async Task CopyRepositoryAsync(string target)
    {
        var listing = await RunAsync(RepositoryRoot.FullPath, "git", "ls-files", "--cached", "--others", "--exclude-standard");
        if (listing.ExitCode != 0)
        {
            throw new InvalidOperationException("git ls-files failed: " + listing.Output);
        }

        foreach (var relative in listing.Standard.Split('\n', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal))
        {
            var source = Path.Combine(RepositoryRoot.FullPath, relative);
            if (!File.Exists(source))
            {
                continue;
            }

            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }
    }

    private static async Task<Run> RunAsync(string directory, string fileName, params string[] arguments)
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["SCAFFOLD_ROOT"] = directory;
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{fileName} did not start");
        var standard = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new Run(process.ExitCode, await standard, await error);
    }

    /// <summary>How a process ended and what it printed.</summary>
    private sealed record Run(int ExitCode, string Standard, string Error)
    {
        public string Output => Standard + Error;
    }
}
