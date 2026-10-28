using System.Reflection;

namespace Frappe.Migrations;

// Raw SQL lives in .sql files embedded under their file name, so it is reviewed as SQL.
internal static class EmbeddedSql
{
    public static string Read(string fileName)
    {
        using Stream stream =
            Assembly.GetExecutingAssembly().GetManifestResourceStream(fileName)
            ?? throw new InvalidOperationException($"No embedded SQL file named {fileName}.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
