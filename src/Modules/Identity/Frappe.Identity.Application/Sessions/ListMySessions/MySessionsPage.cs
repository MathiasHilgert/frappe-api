namespace Frappe.Identity.Application.Sessions.ListMySessions;

/// <summary>One page of the caller's sessions.</summary>
/// <param name="Items">The sessions of the page, newest opened first.</param>
/// <param name="Next">Where the page ended when there are more sessions after it; none on the last page.</param>
public sealed record MySessionsPage(IReadOnlyList<MySessionView> Items, SessionPosition? Next);
