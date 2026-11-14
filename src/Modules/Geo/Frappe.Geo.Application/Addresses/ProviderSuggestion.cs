namespace Frappe.Geo.Application.Addresses;

/// <summary>One suggestion as the provider gives it: its own place id, the full text, and its main and secondary parts.</summary>
public sealed record ProviderSuggestion(string ProviderId, string Text, string MainText, string? SecondaryText);
