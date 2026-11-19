using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>A sample query whose handler gives up on its own, without the caller cancelling.</summary>
public sealed record TimedOutQuery(string Marker) : IQuery<string>;
