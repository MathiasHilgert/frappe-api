using System.Diagnostics.Metrics;

namespace Frappe.Messaging;

/// <summary>
/// Instruments of the messaging platform on meter <c>Frappe.Messaging</c> (<see cref="MessagingTelemetry.Name"/>),
/// which hosts export through <c>Frappe.*</c>. Created once through <see cref="IMeterFactory"/>.
/// </summary>
public sealed class MessagingMetrics
{
    /// <summary>Counter: rows the retention purge deleted, by module and table.</summary>
    public const string PurgedMetric = "frappe.messaging.purged";

    /// <summary>The module label, for example <c>geo</c>.</summary>
    public const string ModuleTag = "frappe.module";

    /// <summary>The table label: <c>outbox_messages</c> or <c>inbox_messages</c>.</summary>
    public const string TableTag = "frappe.messaging.table";

    private readonly Counter<long> _purged;

    public MessagingMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        var meter = meters.Create(MessagingTelemetry.Name);
        _purged = meter.CreateCounter<long>(
            PurgedMetric,
            unit: "{row}",
            description: "Outbox and inbox rows the retention purge deleted.");
    }

    /// <summary><paramref name="rows"/> rows of <paramref name="module"/>'s <paramref name="table"/> were purged.</summary>
    public void Purged(string module, string table, long rows) =>
        _purged.Add(rows, new(ModuleTag, module), new(TableTag, table));
}
