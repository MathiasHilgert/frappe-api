using Frappe.Access.Contracts;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Access.UnitTests.Fixtures;

/// <summary>A guarded command whose rule throws.</summary>
public sealed record ExplodingGuardCommand : ICommand<Unit>, IGuarded
{
    public static CallerRule Allowed => new ThrowingRule();
}
