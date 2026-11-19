namespace Frappe.Testing;

/// <summary>
/// The repository's root directory, found from the test app's output directory: the nearest ancestor holding
/// <c>Frappe.slnx</c>. Tests read and write committed files (contracts, deployment configuration) relative to it.
/// </summary>
public static class RepositoryRoot
{
    private static readonly Lazy<string> Root = new(Find);

    /// <summary>The absolute path of the repository root.</summary>
    public static string FullPath => Root.Value;

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Frappe.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Frappe.slnx not found above " + AppContext.BaseDirectory);
    }
}
