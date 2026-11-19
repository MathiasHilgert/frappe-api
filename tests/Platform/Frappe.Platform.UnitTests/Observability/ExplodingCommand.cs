using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>A sample command whose handler throws.</summary>
public sealed record ExplodingCommand(string Marker) : ICommand<Unit>;
