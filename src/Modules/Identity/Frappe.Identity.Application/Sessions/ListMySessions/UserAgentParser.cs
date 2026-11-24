namespace Frappe.Identity.Application.Sessions.ListMySessions;

/// <summary>
/// Reads the browser and the operating system out of the user agent a session kept as sent (S13). It knows the major ones only and guesses
/// nothing else: what it does not recognize is <see langword="null"/>. The order of the checks matters, because most browsers claim to be
/// several others (Edge and Opera say Chrome, Chrome says Safari, Android says Linux).
/// </summary>
internal static class UserAgentParser
{
    private static readonly (string Marker, string Name)[] Browsers =
    [
        ("Edg/", "Edge"),
        ("EdgA/", "Edge"),
        ("EdgiOS/", "Edge"),
        ("OPR/", "Opera"),
        ("OPiOS/", "Opera"),
        ("Opera", "Opera"),
        ("Firefox/", "Firefox"),
        ("FxiOS/", "Firefox"),
        ("Chrome/", "Chrome"),
        ("CriOS/", "Chrome"),
        ("Safari/", "Safari"),
    ];

    private static readonly (string Marker, string Name)[] Systems =
    [
        ("Windows", "Windows"),
        ("Android", "Android"),
        ("iPhone", "iOS"),
        ("iPad", "iOS"),
        ("iPod", "iOS"),
        ("Macintosh", "macOS"),
        ("Mac OS X", "macOS"),
        ("CrOS", "Chrome OS"),
        ("Linux", "Linux"),
        ("X11", "Linux"),
    ];

    /// <summary>The browser and the system <paramref name="userAgent"/> names; each is <see langword="null"/> when not recognized.</summary>
    public static ClientAgent Parse(string? userAgent) =>
        string.IsNullOrWhiteSpace(userAgent) ? new ClientAgent(null, null) : new ClientAgent(First(Browsers, userAgent), First(Systems, userAgent));

    private static string? First((string Marker, string Name)[] known, string userAgent)
    {
        foreach (var (marker, name) in known)
        {
            if (userAgent.Contains(marker, StringComparison.Ordinal))
            {
                return name;
            }
        }

        return null;
    }
}
