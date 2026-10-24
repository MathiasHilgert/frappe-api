namespace Frappe.Identity.Infrastructure.Endpoints;

/// <summary>A partial update of the profile; only the locale is editable today.</summary>
internal sealed record UpdateMeRequest(string? Locale);
