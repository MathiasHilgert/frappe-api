using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Dispatching;

/// <summary>The result of the resolve step: the recipient can be reached, or a reference no directory could resolve (S3).</summary>
public closed record Resolution
{
    private protected Resolution()
    {
    }

    /// <summary>The recipient and the contacts to reach them, the preferred first.</summary>
    public sealed record Reachable(IReadOnlyList<ResolvedContact> Contacts) : Resolution;

    /// <summary>The reference has no contact.</summary>
    public sealed record Unresolved(RecipientKey Key) : Resolution;
}
