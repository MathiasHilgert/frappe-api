namespace Frappe.Geo.Domain.Holidays;

/// <summary>
/// The credit a source's terms ask for wherever its data is shown: its <see cref="Name"/>, a working link to it
/// (<see cref="Url"/>) and the <see cref="License"/> it publishes under, when it names one.
/// </summary>
public sealed record Attribution
{
    public const int MaxNameLength = 100;

    public const int MaxLicenseLength = 50;

    public Attribution(string name, Uri url, string? license = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(url);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(name.Length, MaxNameLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(license?.Length ?? 0, MaxLicenseLength);
        Name = name;
        Url = url;
        License = license;
    }

    public string Name { get; }

    public Uri Url { get; }

    public string? License { get; }
}
