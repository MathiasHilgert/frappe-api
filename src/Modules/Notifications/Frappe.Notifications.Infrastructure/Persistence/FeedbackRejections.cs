using Frappe.Notifications.Infrastructure.ProviderEvents;
using Frappe.Notifications.Infrastructure.Suppressions;
using Frappe.Persistence;

namespace Frappe.Notifications.Infrastructure.Persistence;

/// <summary>
/// What a refused commit means for the provider's feedback (UOW-05). Each case is a writer that got there first with the same fact: the
/// same event handled twice at once, the same address suppressed by two events at once (a hard bounce and the provider's own suppression
/// arrive back to back), or a delivery moved by another event. Nothing is written, and the call is answered a conflict, so the provider
/// redelivers it: by then the first writer's work is visible and the event is recognized or applied on top of it.
/// </summary>
internal static class FeedbackRejections
{
    public static void Register(CommitRejectionMap rejections)
    {
        ArgumentNullException.ThrowIfNull(rejections);
        rejections
            .On(ProviderEventConfiguration.PrimaryKey, Conflict)
            .On(SuppressionConfiguration.PrimaryKey, Conflict)
            .OnConcurrency(Conflict);
    }

    private static CommitResolution Conflict(CommitFailure failure) =>
        CommitResolution.Reject(new CommitConflictException("Another writer recorded the same feedback first.", failure.Exception));
}
