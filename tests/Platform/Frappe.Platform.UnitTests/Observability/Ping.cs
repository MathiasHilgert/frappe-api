using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>A sample query that succeeds.</summary>
public sealed record Ping(string Marker) : IQuery<string>;
