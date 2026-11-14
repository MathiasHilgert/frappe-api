namespace Frappe.Geo.Http.Addresses;

/// <summary>The suggestions for what the user typed so far, best first.</summary>
public sealed record AddressSuggestionListResponse(IReadOnlyList<AddressSuggestionResponse> Suggestions);
