namespace Frappe.Geo.Global.Places;

/// <summary>
/// <c>Geo:Google:Places</c>: the Places API (New) key and endpoint. The key is optional: without one every address answers
/// unavailable and the host still starts. It comes from the environment or a secret store, never from a committed file.
/// </summary>
public sealed class GooglePlacesOptions
{
    public const string Section = "Geo:Google:Places";

    /// <summary>Places API (New), version 1.</summary>
    public static readonly Uri DefaultBaseUrl = new("https://places.googleapis.com/v1/");

    /// <summary><c>Geo:Google:Places:ApiKey</c>, sent as <c>X-Goog-Api-Key</c>; restrict it to the Places API (New).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Where the API answers, ending in '/'; only tests and proxies change it.</summary>
    public Uri BaseUrl { get; set; } = DefaultBaseUrl;
}
