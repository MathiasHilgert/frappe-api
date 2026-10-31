using System.Reflection;
using System.Text.RegularExpressions;

namespace Frappe.Geo.UnitTests;

// Deferred decisions sit at their plug-in point as "TODO: <what> (<why or when>)", never naming a planning task.
public sealed partial class DeferralTodoTests
{
    [Fact]
    public void Every_geo_todo_states_what_is_deferred_and_why_without_a_task_id()
    {
        string[] malformed =
        [
            .. GeoSources()
                .SelectMany(static file => Todos(File.ReadAllLines(file)))
                .Where(static todo =>
                    !todo.StartsWith("// TODO: ", StringComparison.Ordinal)
                    || !todo.Contains(" (", StringComparison.Ordinal)
                    || !todo.EndsWith(')')
                    || TaskId().IsMatch(todo)
                ),
        ];

        malformed.ShouldBeEmpty();
    }

    // A TODO with the comment lines that continue it, as one line.
    private static IEnumerable<string> Todos(string[] lines)
    {
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            if (!line.Contains("TODO", StringComparison.Ordinal))
            {
                continue;
            }

            while (
                index + 1 < lines.Length
                && lines[index + 1].TrimStart().StartsWith("// ", StringComparison.Ordinal)
            )
            {
                line += " " + lines[++index].Trim()[3..];
            }

            yield return line;
        }
    }

    // From the test project directory recorded at build time: CallerFilePath is path-mapped in CI builds.
    private static IEnumerable<string> GeoSources()
    {
        string project =
            typeof(DeferralTodoTests)
                .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(static entry =>
                    string.Equals(entry.Key, "SnapshotRoot", StringComparison.Ordinal)
                )
                .Value
            ?? "";
        return Directory
            .EnumerateFiles(
                Path.GetFullPath(Path.Combine(project, "../../../../src/Modules/Geo")),
                "*.cs",
                SearchOption.AllDirectories
            )
            .Where(static file =>
                !file.Contains("/obj/", StringComparison.Ordinal)
                && !file.Contains("/bin/", StringComparison.Ordinal)
            );
    }

    [GeneratedRegex(@"\bT[0-9]+\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TaskId();
}
