using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>A sample query that waits until it is cancelled.</summary>
public sealed record SlowQuery(string Marker) : IQuery<string>;
