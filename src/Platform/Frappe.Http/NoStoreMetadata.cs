namespace Frappe.Http;

/// <summary>Marks an endpoint whose successful responses no cache may keep; documentation reads it to declare the header.</summary>
#pragma warning disable S2094 // Endpoint metadata is looked up by type; the marker carries no data on purpose.
public sealed class NoStoreMetadata;
#pragma warning restore S2094
