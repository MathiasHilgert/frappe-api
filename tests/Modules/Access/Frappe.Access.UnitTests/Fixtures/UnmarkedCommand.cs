using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Access.UnitTests.Fixtures;

/// <summary>A command that declares no marker.</summary>
public sealed record UnmarkedCommand : ICommand<Unit>;
