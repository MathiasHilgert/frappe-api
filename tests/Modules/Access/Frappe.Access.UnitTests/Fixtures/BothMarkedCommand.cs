using Frappe.Access.Contracts;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Access.UnitTests.Fixtures;

/// <summary>A command that declares both markers, which says nothing about who may run it.</summary>
public sealed record BothMarkedCommand : ICommand<Unit>, IGuarded, IPublic
{
    public static CallerRule Allowed => Is.Authenticated;
}
