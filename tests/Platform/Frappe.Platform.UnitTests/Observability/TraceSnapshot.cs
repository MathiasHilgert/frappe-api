namespace Frappe.Platform.UnitTests.Observability;

/// <summary>What a use case saw of its trace after awaiting: the work span it started and its own ambient marker.</summary>
public sealed record TraceSnapshot(string TraceId, string ParentSpanId, string? Marker);
