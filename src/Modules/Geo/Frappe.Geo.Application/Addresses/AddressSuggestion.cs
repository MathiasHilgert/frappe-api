namespace Frappe.Geo.Application.Addresses;

/// <summary>A suggestion as served: an opaque id to resolve it, the full text, and its main and secondary parts for display.</summary>
public sealed record AddressSuggestion(string Id, string Text, string MainText, string? SecondaryText);
