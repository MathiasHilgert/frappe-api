using Frappe.Access.Contracts;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Access.UnitTests.Fixtures;

/// <summary>A guarded command whose handler must check a <see cref="Note"/>.</summary>
public sealed record EditNoteCommand : ICommand<Unit>, IGuarded, IActsOn<Note>
{
    public static CallerRule Allowed => Is.Authenticated;
}
