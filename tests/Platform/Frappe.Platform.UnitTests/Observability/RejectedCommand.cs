using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>A sample command the handler rejects with a business error.</summary>
public sealed record RejectedCommand(string Marker) : ICommand<Unit>;
