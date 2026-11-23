namespace Frappe.Http;

/// <summary>Marks an endpoint whose successful responses may be cached; documentation reads it to declare the headers.</summary>
public sealed record CacheableMetadata(TimeSpan MaxAge);
