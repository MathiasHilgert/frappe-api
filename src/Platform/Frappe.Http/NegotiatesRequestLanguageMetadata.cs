namespace Frappe.Http;

/// <summary>
/// Marks an endpoint whose response depends on the language the client asks for; documentation reads it to declare
/// the <c>Accept-Language</c> header.
/// </summary>
#pragma warning disable S2094 // Endpoint metadata is looked up by type; the marker carries no data on purpose.
public sealed class NegotiatesRequestLanguageMetadata;
#pragma warning restore S2094
