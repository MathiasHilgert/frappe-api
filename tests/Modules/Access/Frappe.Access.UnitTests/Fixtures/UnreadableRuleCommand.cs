using Frappe.Access.Contracts;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Access.UnitTests.Fixtures;

/// <summary>A guarded command whose <c>Allowed</c> getter throws, so no rule can be read from it.</summary>
public sealed record UnreadableRuleCommand : ICommand<Unit>, IGuarded
{
    public static CallerRule Allowed => throw new InvalidOperationException("no rule");
}
