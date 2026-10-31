namespace Frappe.Geo.Infrastructure.Seed;

/// <summary>Reads the seed files: UTF-8, tab-separated, LF lines, a fixed header, no quoting.</summary>
internal static class Tsv
{
    /// <summary>Reads an embedded seed file whose first line must be <paramref name="header"/>.</summary>
    public static IReadOnlyList<string[]> Read(string fileName, IReadOnlyList<string> header)
    {
        using Stream stream =
            typeof(Tsv).Assembly.GetManifestResourceStream(fileName)
            ?? throw new InvalidOperationException($"No embedded seed file named {fileName}.");
        using StreamReader reader = new(stream);
        return Parse(reader, fileName, header);
    }

    /// <summary>Parses seed rows; a different header or a row with another field count or no row at all fails the seed.</summary>
    public static IReadOnlyList<string[]> Parse(
        TextReader reader,
        string fileName,
        IReadOnlyList<string> header
    )
    {
        string expected = string.Join('\t', header);
        string? first = reader.ReadLine();
        if (!string.Equals(first, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{fileName} starts with '{first}' instead of the header '{expected}'."
            );
        }

        List<string[]> rows = [];
        int line = 1;
        while (reader.ReadLine() is { } text)
        {
            line++;
            if (text.Length == 0)
            {
                continue;
            }

            string[] fields = text.Split('\t');
            if (fields.Length != header.Count)
            {
                throw new InvalidDataException(
                    $"{fileName} line {line} has {fields.Length} fields instead of {header.Count}."
                );
            }

            rows.Add(fields);
        }

        // The seed retires every row a file leaves out, so a file without rows would retire its whole catalogue.
        return rows.Count > 0
            ? rows
            : throw new InvalidDataException($"{fileName} holds a header but no rows.");
    }
}
