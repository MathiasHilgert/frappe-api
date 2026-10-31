using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Frappe.TestUtilities;

/// <summary>
/// Compares a JSON contract with its committed golden file, <c>&lt;name&gt;.snapshot.json</c> next to the test, after
/// normalizing volatile values (trace ids, GUIDs, timestamps). A mismatch writes <c>&lt;name&gt;.received.json</c>
/// beside it for diffing; <c>FRAPPE_UPDATE_SNAPSHOTS=1</c> rewrites the snapshot instead. When <c>CI</c> is set
/// nothing is written and the difference is in the failure message.
/// </summary>
public static class Snapshot
{
    private static readonly JsonSerializerOptions _indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Fails unless <paramref name="json"/>, normalized, equals the snapshot named <paramref name="name"/>.</summary>
    /// <param name="json">The contract, such as a response body.</param>
    /// <param name="name">The snapshot name, unique next to the calling test file.</param>
    /// <param name="testFile">The calling test file; filled in by the compiler.</param>
    /// <exception cref="SnapshotMismatchException">The contract differs from the snapshot, or no snapshot exists.</exception>
    // Not inlined, so the calling assembly is the test assembly whose SnapshotRoot locates the files.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ShouldMatch(string json, string name, [CallerFilePath] string testFile = "")
    {
        ArgumentNullException.ThrowIfNull(json);
        string directory = DirectoryOf(Assembly.GetCallingAssembly(), testFile);
        string snapshot = Path.Combine(directory, $"{name}.snapshot.json");
        string received = Path.Combine(directory, $"{name}.received.json");
        string actual = Normalize(json);
        string? expected = File.Exists(snapshot) ? File.ReadAllText(snapshot) : null;
        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            File.Delete(received);
            return;
        }

        // CI never writes: the build server's tree is thrown away, so the difference goes in the message.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
        {
            throw new SnapshotMismatchException(
                $"{name} differs from {snapshot}:\n{Difference(expected ?? "", actual)}\nReceived:\n{actual}"
            );
        }

        if (
            string.Equals(
                Environment.GetEnvironmentVariable("FRAPPE_UPDATE_SNAPSHOTS"),
                "1",
                StringComparison.Ordinal
            )
        )
        {
            File.WriteAllText(snapshot, actual);
            File.Delete(received);
            return;
        }

        File.WriteAllText(received, actual);
        throw new SnapshotMismatchException(
            expected is null
                ? $"No snapshot {snapshot}; review {received} and commit it as the snapshot."
                : $"{name} differs from its snapshot; diff {received} against {snapshot}."
        );
    }

    /// <summary>Formats the JSON indented with LF line ends and replaces its volatile values by placeholders.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The normalized JSON, ending in a line feed.</returns>
    public static string Normalize(string json)
    {
        var root = JsonNode.Parse(json);
        Scrub(root, propertyName: null);
        return root?.ToJsonString(_indented).ReplaceLineEndings("\n") + "\n";
    }

    // The test file's directory under the project directory recorded at build time: CallerFilePath alone is
    // path-mapped (/_/...) in CI builds, but its part below the project directory is still right.
    private static string DirectoryOf(Assembly testAssembly, string testFile)
    {
        string root =
            testAssembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(static entry =>
                    string.Equals(entry.Key, "SnapshotRoot", StringComparison.Ordinal)
                )
                ?.Value
            ?? throw new InvalidOperationException(
                $"{testAssembly.GetName().Name} records no SnapshotRoot metadata."
            );
        string marker = "/" + Path.GetFileName(root) + "/";
        string file = testFile.Replace('\\', '/');
        int index = file.LastIndexOf(marker, StringComparison.Ordinal);
        string relative =
            index < 0 ? "" : Path.GetDirectoryName(file[(index + marker.Length)..]) ?? "";
        return Path.Combine(root, relative);
    }

    // The first differing line, numbered, so a CI log shows what changed without the files.
    private static string Difference(string expected, string actual)
    {
        string[] before = expected.Split('\n');
        string[] after = actual.Split('\n');
        for (int line = 0; line < Math.Max(before.Length, after.Length); line++)
        {
            string left = line < before.Length ? before[line] : "<end>";
            string right = line < after.Length ? after[line] : "<end>";
            if (!string.Equals(left, right, StringComparison.Ordinal))
            {
                return $"line {line + 1}:\n- {left}\n+ {right}";
            }
        }

        return "no line differs";
    }

    private static void Scrub(JsonNode? node, string? propertyName)
    {
        switch (node)
        {
            case JsonObject members:
                foreach ((string name, JsonNode? value) in members.ToArray())
                {
                    if (value is JsonValue scalar && Placeholder(name, scalar) is { } placeholder)
                    {
                        members[name] = placeholder;
                    }
                    else
                    {
                        Scrub(value, name);
                    }
                }

                break;
            case JsonArray items:
                for (int index = 0; index < items.Count; index++)
                {
                    if (
                        items[index] is JsonValue scalar
                        && Placeholder(propertyName, scalar) is { } placeholder
                    )
                    {
                        items[index] = placeholder;
                    }
                    else
                    {
                        Scrub(items[index], propertyName);
                    }
                }

                break;
        }
    }

    private static string? Placeholder(string? propertyName, JsonValue value)
    {
        if (!value.TryGetValue(out string? text))
        {
            return null;
        }

        if (string.Equals(propertyName, "traceId", StringComparison.Ordinal))
        {
            return "{traceId}";
        }

        if (Guid.TryParseExact(text, "D", out _))
        {
            return "{guid}";
        }

        // A timestamp has a time part; a plain date such as 2026-04-02 is contract data and stays.
        return
            text.Contains('T', StringComparison.Ordinal)
            && DateTimeOffset.TryParse(
                text,
                System.Globalization.CultureInfo.InvariantCulture,
                out _
            )
            ? "{timestamp}"
            : null;
    }
}
