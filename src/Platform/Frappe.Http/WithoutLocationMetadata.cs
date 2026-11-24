namespace Frappe.Http;

/// <summary>Marks an endpoint whose <c>201</c> or <c>202</c> answer has no <c>Location</c>; documentation leaves the header out.</summary>
#pragma warning disable S2094 // Endpoint metadata is looked up by type; the marker carries no data on purpose.
public sealed class WithoutLocationMetadata;
#pragma warning restore S2094
