using Frappe.Access.Contracts;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Access.UnitTests.Fixtures;

/// <summary>A guarded command that needs a recent step-up.</summary>
public sealed record ChangeNoteCommand : ICommand<Unit>, IGuarded
{
    public static CallerRule Allowed => Is.Authenticated & Has.RecentStepUp;
}
