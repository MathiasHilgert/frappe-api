namespace Frappe.Http;

/// <summary>Marks an endpoint whose successful responses a cache may keep only after revalidating them; documentation reads it to declare the
/// headers.</summary>
#pragma warning disable S2094 // Endpoint metadata is looked up by type; the marker carries no data on purpose.
public sealed class NoCacheMetadata;
#pragma warning restore S2094
