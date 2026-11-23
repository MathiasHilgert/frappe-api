namespace Frappe.Http;

/// <summary>Marks an endpoint that accepts an <c>Idempotency-Key</c>; documentation reads it to declare the header and its answers.</summary>
#pragma warning disable S2094 // Endpoint metadata is looked up by type; the marker carries no data on purpose.
public sealed class IdempotentMetadata;
#pragma warning restore S2094
