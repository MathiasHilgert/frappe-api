using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>A sample query that reports the trace it runs in.</summary>
public sealed record TracedQuery : IQuery<TraceSnapshot>;
