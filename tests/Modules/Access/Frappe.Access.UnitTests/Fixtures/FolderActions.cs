using Frappe.Access.Contracts;

namespace Frappe.Access.UnitTests.Fixtures;

/// <summary>The actions of <see cref="Folder"/>.</summary>
public static class FolderActions
{
    public static readonly ResourceAction<Folder> ArchiveStepUpFirst = new("archive-step-up-first");

    public static readonly ResourceAction<Folder> ArchiveStepUpLast = new("archive-step-up-last");

    public static readonly ResourceAction<Folder> Edit = new("edit");

    public static readonly ResourceAction<Folder> Explode = new("explode");
}
