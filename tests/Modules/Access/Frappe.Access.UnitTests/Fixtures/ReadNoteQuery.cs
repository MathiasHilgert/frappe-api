using Frappe.Access.Contracts;
using Frappe.UseCases;

namespace Frappe.Access.UnitTests.Fixtures;

/// <summary>A guarded query: any authenticated principal.</summary>
public sealed record ReadNoteQuery : IQuery<string>, IGuarded
{
    public static CallerRule Allowed => Is.Authenticated;
}
