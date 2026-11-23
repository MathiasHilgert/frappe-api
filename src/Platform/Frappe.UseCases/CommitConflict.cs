namespace Frappe.UseCases;

/// <summary>The error code a caller is answered when other writers kept changing the same data while a use case committed.</summary>
public static class CommitConflict
{
    /// <summary>Answered with a 409: nothing was written and the same request may succeed when sent again.</summary>
    public const string Code = "platform.persistence.commit_conflict";
}
