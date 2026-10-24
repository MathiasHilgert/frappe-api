using System.Diagnostics.Metrics;

namespace Frappe.Notification.Infrastructure.Mails;

/// <summary>Notification's meter: mails that will never be delivered show on dashboards, not only in the error queue.</summary>
internal sealed class NotificationMetrics
{
    public const string MeterName = "Frappe.Notification";
    public const string MailDeadLetteredName = "frappe.notification.mail.dead_lettered";

    private readonly Counter<long> _deadLettered;

    public NotificationMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _deadLettered = meterFactory.Create(MeterName).CreateCounter<long>(MailDeadLetteredName,
            description: "Mails moved to the dead-letter queue, tagged by reason (rejected or exhausted).");
    }

    public void DeadLettered(string reason) => _deadLettered.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
