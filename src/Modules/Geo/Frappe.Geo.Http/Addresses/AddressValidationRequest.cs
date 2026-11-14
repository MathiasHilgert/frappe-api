namespace Frappe.Geo.Http.Addresses;

/// <summary>
/// The address to validate: a suggestion's <paramref name="Reference"/> (its <c>id</c>) or one kept with a snapshot, the
/// search <paramref name="Session"/> it closes, and optionally the <paramref name="Language"/> of the answer (BCP 47,
/// else <c>Accept-Language</c>).
/// </summary>
public sealed record AddressValidationRequest(string? Reference, string? Session, string? Language = null);
