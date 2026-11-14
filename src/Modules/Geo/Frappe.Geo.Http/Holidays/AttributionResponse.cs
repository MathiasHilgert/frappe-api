using Frappe.Geo.Domain.Holidays;

namespace Frappe.Geo.Http.Holidays;

/// <summary>A source to credit: its name, a working link to it and its license, when it names one.</summary>
public sealed record AttributionResponse(string Name, Uri Url, string? License)
{
    internal static AttributionResponse Of(Attribution a) => new(a.Name, a.Url, a.License);
}
