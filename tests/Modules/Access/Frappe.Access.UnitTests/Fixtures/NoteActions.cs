using Frappe.Access.Contracts;

namespace Frappe.Access.UnitTests.Fixtures;

/// <summary>The actions of <see cref="Note"/>.</summary>
public static class NoteActions
{
    public static readonly ResourceAction<Note> Read = new("read");

    public static readonly ResourceAction<Note> Change = new("change");

    public static readonly ResourceAction<Note> Undeclared = new("undeclared");
}
