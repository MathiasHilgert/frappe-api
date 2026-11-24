using Frappe.Notifications.Domain.Preferences;
using Frappe.Notifications.Infrastructure.Preferences;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Notifications.Infrastructure.Persistence;

/// <summary>
/// What a refused commit means for an unsubscribe (UOW-05). The recipient already opted out in a call that committed a moment before (the
/// same link opened twice at once): the row they asked for exists, so the second call drops its own and succeeds, answering what a repeat
/// answers. The unit collected the dropped row's event before the save, so the counter over-counts by one in this race.
/// </summary>
internal static class PreferenceRejections
{
    public static void Register(CommitRejectionMap rejections)
    {
        ArgumentNullException.ThrowIfNull(rejections);
        rejections.On(PreferenceConfiguration.PrimaryKey, AlreadyOptedOut);
    }

    private static CommitResolution AlreadyOptedOut(CommitFailure failure)
    {
        foreach (var entry in failure.Context.ChangeTracker.Entries<Preference>().Where(entry => entry.State == EntityState.Added))
        {
            entry.State = EntityState.Detached;
        }

        return CommitResolution.Repair;
    }
}
