using System.Xml.Linq;
using Frappe.Testing;

namespace Frappe.Architecture.Tests;

/// <summary>
/// The solution file and the folders agree: every project Frappe.slnx lists exists, every project on disk is listed, and
/// every project restores from a committed lock file (STR-01).
/// </summary>
public sealed class SolutionTests
{
    private static readonly string[] Roots = ["src", "tests"];

    private static IEnumerable<string> Listed() =>
        XDocument.Load(Path.Combine(RepositoryRoot.FullPath, "Frappe.slnx")).Descendants("Project").Select(project => (string)project.Attribute("Path")!);

    private static IEnumerable<string> OnDisk() =>
        Roots.Select(root => Path.Combine(RepositoryRoot.FullPath, root))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(RepositoryRoot.FullPath, path).Replace('\\', '/'));

    [Test]
    public async Task Every_project_the_solution_lists_exists()
    {
        // Act
        var missing = Listed().Where(path => !File.Exists(Path.Combine(RepositoryRoot.FullPath, path))).ToList();

        // Assert
        await Assert.That(missing).IsEmpty().Because("listed in Frappe.slnx but not on disk: " + string.Join(", ", missing));
    }

    [Test]
    public async Task Every_project_on_disk_is_listed_in_the_solution()
    {
        // Act
        var unlisted = OnDisk().Except(Listed(), StringComparer.Ordinal).ToList();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(Listed()).Contains("tests/Frappe.Architecture.Tests/Frappe.Architecture.Tests.csproj");
            await Assert.That(unlisted).IsEmpty().Because("on disk but missing from Frappe.slnx: " + string.Join(", ", unlisted));
        }
    }

    [Test]
    public async Task Every_project_has_its_committed_lock_file()
    {
        // Act
        var unlocked = OnDisk()
            .Where(path => !File.Exists(Path.Combine(RepositoryRoot.FullPath, Path.GetDirectoryName(path)!, "packages.lock.json")))
            .ToList();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(OnDisk()).IsNotEmpty();
            await Assert.That(unlocked).IsEmpty().Because("restore the solution to write packages.lock.json: " + string.Join(", ", unlocked));
        }
    }
}
