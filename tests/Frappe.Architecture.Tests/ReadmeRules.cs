using System.Text.RegularExpressions;

namespace Frappe.Architecture.Tests;

/// <summary>
/// The pure checks behind STR-14: a module's <c>README.md</c> says what its code does. Each takes the README text and the facts read off the code
/// (instruments, extension projects, a file-exists probe), so a test can run it on the real module and on a small drifted fixture (the rule can
/// see).
/// </summary>
internal static partial class ReadmeRules
{
    /// <summary>The body of the section whose heading starts with <c>## {heading}</c>, up to the next <c>## </c> heading; empty if absent.</summary>
    internal static string Section(string readme, string heading)
    {
        var lines = readme.ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, line => line.StartsWith($"## {heading}", StringComparison.Ordinal));
        if (start < 0)
        {
            return string.Empty;
        }

        var body = lines.Skip(start + 1).TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal));
        return string.Join('\n', body);
    }

    /// <summary>The relative links that point nowhere, one line each. Web links and anchors are skipped; a <c>#fragment</c> is dropped.</summary>
    internal static string[] BrokenLinks(string readme, Func<string, bool> exists) =>
    [
        .. Link().Matches(readme)
            .Select(match => match.Groups["target"].Value)
            .Where(target => !target.Contains("://", StringComparison.Ordinal) && !target.StartsWith('#'))
            .Select(target => target.Split('#')[0])
            .Where(target => !exists(target))
            .Distinct(StringComparer.Ordinal)
            .Select(target => $"{target}: no such file or folder"),
    ];

    /// <summary>
    /// How the <c>## Metrics</c> table and the instruments the module's metrics classes publish disagree, one line each: an instrument the
    /// table lacks, a row no instrument backs, or a row whose Class column names another class.
    /// </summary>
    internal static string[] MetricDrift(string readme, IEnumerable<(string Name, string Class)> instruments)
    {
        var documented = MetricRow().Matches(Section(readme, "Metrics"))
            .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["class"].Value, StringComparer.Ordinal);
        var published = instruments.ToDictionary(instrument => instrument.Name, instrument => instrument.Class, StringComparer.Ordinal);

        return
        [
            .. published.Keys.Except(documented.Keys).Select(name => $"{name}: not in the README"),
            .. documented.Keys.Except(published.Keys).Select(name => $"{name}: in the README, not in the code"),
            .. documented.Where(row => published.TryGetValue(row.Key, out var owner) && owner != row.Value)
                .Select(row => $"{row.Key}: the README names {row.Value}, the code {published[row.Key]}"),
        ];
    }

    /// <summary>How the <c>## Extensions</c> table and the projects under <c>Extensions/</c> disagree, one line each, by project name.</summary>
    internal static string[] ExtensionDrift(string readme, IEnumerable<string> projects)
    {
        var linked = Link().Matches(Section(readme, "Extensions"))
            .Select(match => Path.GetFileName(match.Groups["target"].Value.TrimEnd('/')))
            .ToHashSet(StringComparer.Ordinal);
        var present = projects.ToHashSet(StringComparer.Ordinal);

        return
        [
            .. present.Except(linked).Order(StringComparer.Ordinal).Select(project => $"{project}: not in the README"),
            .. linked.Except(present).Order(StringComparer.Ordinal).Select(project => $"{project}: in the README, not under Extensions/"),
        ];
    }

    [GeneratedRegex(@"\[(?<text>[^\]]+)\]\((?<target>[^)\s]+)\)")]
    private static partial Regex Link();

    /// <summary>A metrics row: the backticked name in the first cell, the linked class in the last.</summary>
    [GeneratedRegex(@"^\|\s*`(?<name>[^`]+)`\s*\|.*\|\s*\[(?<class>[^\]]+)\]\([^)]*\)\s*\|\s*$", RegexOptions.Multiline)]
    private static partial Regex MetricRow();
}
