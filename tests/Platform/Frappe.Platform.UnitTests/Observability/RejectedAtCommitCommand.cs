using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>A sample command whose commit is rejected.</summary>
public sealed record RejectedAtCommitCommand(string Marker) : ICommand<Unit>;
