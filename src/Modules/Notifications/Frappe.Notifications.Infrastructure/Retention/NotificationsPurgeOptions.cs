namespace Frappe.Notifications.Infrastructure.Retention;

/// <summary>
/// How long Notifications keeps what it only needs for a while (S11), bound from <see cref="Section"/>, the section of the purge job whose
/// own <c>Cron</c> key schedules it. Suppressions and preferences are never purged.
/// </summary>
public sealed class NotificationsPurgeOptions
{
    /// <summary>The configuration section of the nightly purge: <c>Notifications:Purge</c>.</summary>
    public const string Section = "Notifications:Purge";

    /// <summary>The longest retention accepted, in days: ten years.</summary>
    public const int MaxDays = 3650;

    /// <summary>
    /// <c>Notifications:Purge:DeliveryDays</c>: whole days after which a delivery record is deleted, counted from its creation. Default 90.
    /// Range 1 to 3650. Raising it keeps the delivery history longer and grows the table; lowering it forgets sooner. A deleted delivery
    /// can no longer be matched by a late provider event, which is then parked and purged like any unmatched one.
    /// </summary>
    public int DeliveryDays { get; set; } = 90;

    /// <summary>
    /// <c>Notifications:Purge:ProviderEventDays</c>: whole days after which a handled or parked provider event is deleted, counted from its
    /// arrival. Default 7. Range 1 to 3650. It must outlast the provider's own redelivery window or a redelivered event would count twice.
    /// </summary>
    public int ProviderEventDays { get; set; } = 7;

    /// <summary>
    /// <c>Notifications:Purge:SendRequestDays</c>: whole days after which a record of an accepted explicit send request is deleted, counted
    /// from its acceptance. Default 90. Range 1 to 3650. The record only dedupes a repeated request, so it must outlast the window in which a
    /// caller can repeat one.
    /// </summary>
    public int SendRequestDays { get; set; } = 90;

    /// <summary>
    /// <c>Notifications:Purge:BatchSize</c>: the most rows one delete statement removes, so no statement holds its locks for long. Default 1000.
    /// Range 1 or more. Raising it purges faster with longer locks; lowering it does the opposite.
    /// </summary>
    public int BatchSize { get; set; } = 1000;
}
