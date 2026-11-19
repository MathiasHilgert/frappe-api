using System.Text.RegularExpressions;
using Frappe.Testing;

namespace Frappe.Architecture.Tests;

/// <summary>
/// Every type under <c>src</c> and under <c>tests</c> opens with an XML <c>&lt;summary&gt;</c> (CRAFT-08, TEST-07). No compiler
/// rule is scoped to types (CS1591 and SA1600 also demand every member), so the declarations are read from the source.
/// </summary>
public sealed partial class TypeDocumentationTests
{
    [GeneratedRegex(@"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|internal|private|protected|file|static|sealed|abstract|partial|readonly|ref|unsafe)\s+)*(?:class|record|struct|interface|enum)\b", RegexOptions.None, 1000)]
    private static partial Regex TypeDeclaration();

    [Test]
    [Arguments("src")]
    [Arguments("tests")]
    public async Task Every_type_has_a_summary(string folder)
    {
        // Arrange
        var undocumented = new List<string>();
        var root = Path.Combine(RepositoryRoot.FullPath, folder);
        var files = Directory.Exists(root) ? Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories) : [];

        // Act
        foreach (var file in files
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var lines = await File.ReadAllLinesAsync(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!TypeDeclaration().IsMatch(lines[i]))
                {
                    continue;
                }

                var j = i - 1;
                while (j >= 0 && lines[j].TrimStart() is ['[', ..] or ['#', ..] or ['/', '/', not '/', ..])
                {
                    j--;
                }

                var documented = false;
                while (j >= 0 && lines[j].TrimStart().StartsWith("///", StringComparison.Ordinal))
                {
                    documented |= lines[j].Contains("<summary>", StringComparison.Ordinal) || lines[j].Contains("<inheritdoc", StringComparison.Ordinal);
                    j--;
                }

                if (!documented)
                {
                    undocumented.Add($"{Path.GetRelativePath(RepositoryRoot.FullPath, file)}:{i + 1}");
                }
            }
        }

        // Assert
        await Assert.That(undocumented).IsEmpty().Because("types without an XML summary: " + string.Join(", ", undocumented));
    }
}
