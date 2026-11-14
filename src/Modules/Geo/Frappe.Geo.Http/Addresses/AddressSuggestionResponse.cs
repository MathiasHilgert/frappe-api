namespace Frappe.Geo.Http.Addresses;

/// <summary>A suggestion: its opaque id (validate it with POST address-validations), its full text, and its main and secondary parts.</summary>
public sealed record AddressSuggestionResponse(string Id, string Text, string MainText, string? SecondaryText);
