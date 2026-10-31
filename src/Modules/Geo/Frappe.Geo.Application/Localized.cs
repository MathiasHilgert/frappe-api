using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Application;

/// <summary>A read whose names come in one locale: the one they actually resolved to along the fallback chain.</summary>
/// <param name="Value">The read, such as a list of countries.</param>
/// <param name="Locale">The locale of its names, such as <c>en</c> for a request in an unseeded <c>fr</c>.</param>
public sealed record Localized<T>(T Value, Locale Locale);
