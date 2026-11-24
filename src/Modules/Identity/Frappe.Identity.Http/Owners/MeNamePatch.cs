namespace Frappe.Identity.Http.Owners;

/// <summary>The name members a profile patch may change.</summary>
/// <param name="Given">The new given name; it may not be removed.</param>
/// <param name="Family">The new family name; <c>null</c> removes it.</param>
public sealed record MeNamePatch(string? Given, string? Family);
