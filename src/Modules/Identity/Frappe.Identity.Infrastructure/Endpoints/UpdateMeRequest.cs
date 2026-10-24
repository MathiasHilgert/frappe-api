namespace Frappe.Identity.Infrastructure.Endpoints;

/// <summary>A partial update of the profile; only the locale is editable today.</summary>
public sealed record UpdateMeRequest(string? Locale);
