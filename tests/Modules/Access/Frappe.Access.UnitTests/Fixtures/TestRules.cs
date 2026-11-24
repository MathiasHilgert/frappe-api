using Frappe.Access.Contracts;

namespace Frappe.Access.UnitTests.Fixtures;

/// <summary>The rules of <see cref="Note"/> and <see cref="Folder"/>, written as a module writes its own.</summary>
public sealed class TestRules : IRules
{
    public const string NoteNotFound = "test.note.not_found";

    public const string FolderNotFound = "test.folder.not_found";

    public void Define(RuleBook book)
    {
        ArgumentNullException.ThrowIfNull(book);
        book.For<Note>(notFound: NoteNotFound)
            .HeldBy(note => note.HolderId)
            .Visible(Is.Holder)
            .Allow(NoteActions.Read, Is.Holder)
            .Allow(NoteActions.Change, Is.Holder & Has.RecentStepUp);
        book.For<Folder>(notFound: FolderNotFound)
            .HeldBy(folder => folder.HolderId)
            .Visible(Is.Authenticated)
            .Allow(FolderActions.ArchiveStepUpFirst, Has.RecentStepUp & Is.Kind("admin"))
            .Allow(FolderActions.ArchiveStepUpLast, Is.Kind("admin") & Has.RecentStepUp)
            .Allow(FolderActions.Edit, Is.Holder)
            .Allow(FolderActions.Explode, new ThrowingRule());
    }
}
