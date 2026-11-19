using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>A sample command that succeeds.</summary>
public sealed record AcceptedCommand(string Marker) : ICommand<string>;
