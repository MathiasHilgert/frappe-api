using Frappe.Notifications.Application.Telemetry;

namespace Frappe.Notifications.Application.Deliveries;

/// <summary>
/// The deliveries a dispatch decided on and has not counted yet. The dispatcher runs before the claim, outside the transaction that saves
/// its delivery rows, so counting there would count a delivery whose claim loses its race or rolls back (OBS-06). It records here; the
/// consumer's in-transaction step calls <see cref="MarkClaimed"/> (it runs only for the winner of the claim), and its after-commit step calls
/// <see cref="CountIfClaimed"/>. A dispatch that throws is dropped with its scope. A claim lost to a racing delivery runs the after-commit step
/// too, but nothing marked it claimed, so it counts nothing: the winner counted the same outcomes.
/// </summary>
public sealed class PendingDeliveries(NotificationsMetrics metrics)
{
    private readonly List<(string Kind, string Channel, DispatchOutcome Outcome)> _pending = [];

    /// <summary>How many deliveries wait for the commit.</summary>
    public int Count => _pending.Count;

    private bool _claimed;

    internal void Add(string kind, string channel, DispatchOutcome outcome) => _pending.Add((kind, channel, outcome));

    /// <summary>Records that this scope won the claim, so its pending deliveries are the ones to count after the commit.</summary>
    public void MarkClaimed() => _claimed = true;

    /// <summary>Counts the pending deliveries when this scope won the claim, and forgets them either way.</summary>
    public void CountIfClaimed()
    {
        if (_claimed)
        {
            CountCommitted();
        }

        _pending.Clear();
    }

    /// <summary>Counts every pending delivery and forgets it, so a second call counts nothing again.</summary>
    public void CountCommitted()
    {
        foreach (var (kind, channel, outcome) in _pending)
        {
            metrics.Delivery(kind, channel, outcome);
        }

        _pending.Clear();
    }
}
