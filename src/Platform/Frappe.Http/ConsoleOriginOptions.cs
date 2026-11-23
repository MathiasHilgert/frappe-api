namespace Frappe.Http;

/// <summary>
/// The origins of the console, the browser application on another subdomain of the same site (<c>https://app.frappe.la</c>
/// calling <c>https://api.frappe.la</c>), bound from <c>Http:Console:Origins</c>, comma separated, empty by default.
/// Only they get credentialed CORS answers (<see cref="HttpRegistration.ConsolePolicy"/>), and only they may send an
/// unsafe request that carries a browser credential (<see cref="ConsoleOriginCheck"/>). Each entry is an absolute
/// <c>https</c> origin (scheme, host and an optional port, nothing else); the one exception is <c>http</c> on
/// <c>localhost</c> or a loopback address, for a console served by a local development server. An invalid entry fails at
/// startup naming the key and the entry.
/// </summary>
public sealed class ConsoleOriginOptions
{
    /// <summary>The configuration section the hosts bind.</summary>
    public const string Section = "Http:Console";

    /// <summary>
    /// <c>Http:Console:Origins</c>: the console's origins, comma separated, each an absolute <c>https</c> origin
    /// (scheme, host and optional port; <c>http</c> only on a loopback host). Default empty: no origin gets
    /// credentialed CORS and no cross-site browser request may mutate. Range any number of valid entries; an
    /// invalid entry fails at startup. Adding an origin lets that site's pages call the API with the visitor's
    /// cookie, so list only sites you operate.
    /// </summary>
    [ListEntries($"{Section}:{nameof(Origins)}", ListEntryKind.ConsoleOrigin)]
    public string? Origins { get; set; }

    /// <summary>
    /// The valid entries, normalized as a browser sends them in <c>Origin</c> (lower case, no default port). Parsed once,
    /// when the options are validated at startup, and again only if <see cref="Origins"/> changes.
    /// </summary>
    public IReadOnlyList<string> Allowed() => Parsed().List;

    /// <summary>
    /// Whether <paramref name="origin"/> (an <c>Origin</c> header) is one of the console's: a lookup in the parsed set, so
    /// a checked request parses nothing. A browser sends the origin serialized, the way the set holds it; any other
    /// spelling (a path, a default port) is not one.
    /// </summary>
    public bool Allows(string? origin) => origin is not null && Parsed().Set.Contains(origin);

    private (string? Source, string[] List, HashSet<string> Set)? _parsed;

    private (string? Source, string[] List, HashSet<string> Set) Parsed()
    {
        if (_parsed is { } parsed && string.Equals(parsed.Source, Origins, StringComparison.Ordinal))
        {
            return parsed;
        }

        string[] list = [.. ListEntriesAttribute.Entries(Origins).Select(entry =>
            TryNormalize(entry, out var origin) ? origin : null).OfType<string>().Distinct(StringComparer.Ordinal)];
        var fresh = (Origins, list, new HashSet<string>(list, StringComparer.OrdinalIgnoreCase));
        _parsed = fresh;
        return fresh;
    }

    /// <summary>
    /// Reads an origin: <c>https://host[:port]</c>, or <c>http</c> on a loopback host; no user, path, query or fragment.
    /// </summary>
    internal static bool TryNormalize(string entry, out string origin)
    {
        origin = string.Empty;
        if (!Uri.TryCreate(entry, UriKind.Absolute, out var uri)
            || uri.UserInfo.Length > 0
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || uri.AbsolutePath != "/"
            || entry.TrimEnd().EndsWith('/')
            || !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
        {
            return false;
        }

        origin = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        return true;
    }
}
