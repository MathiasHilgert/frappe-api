namespace Frappe.Identity.Infrastructure.Retention;

/// <summary>
/// How long Identity keeps what it only needs for a while (S16), bound from <see cref="Section"/>, the section of the daily job whose own
/// <c>Cron</c> key schedules it.
/// </summary>
public sealed class IdentityRetentionOptions
{
    /// <summary>The configuration section of the daily retention job: <c>Identity:Retention</c>.</summary>
    public const string Section = "Identity:Retention";

    /// <summary>The longest retention accepted, in days: ten years.</summary>
    public const int MaxDays = 3650;

    /// <summary>
    /// <c>Identity:Retention:PendingOwnerDays</c>: whole days after which an owner that never verified their address is deleted with their
    /// data, counted from the registration (a newer registration of the same address restarts it). Default 7. Range 1 to 3650.
    /// </summary>
    public int PendingOwnerDays { get; set; } = 7;

    /// <summary>
    /// <c>Identity:Retention:EndedSessionDays</c>: whole days after which an ended session is deleted, with its truncated address, counted
    /// from the sweep that ended it. Default 30. Range 1 to 3650.
    /// </summary>
    public int EndedSessionDays { get; set; } = 30;

    /// <summary>
    /// <c>Identity:Retention:CodeDays</c>: whole days after which a one-time code is deleted, counted from its expiry. Default 7. Range 1 to 3650.
    /// </summary>
    public int CodeDays { get; set; } = 7;

    /// <summary>
    /// <c>Identity:Retention:BatchSize</c>: the most rows one statement changes, so none holds its locks for long. Default 1000. Range 1 or more.
    /// </summary>
    public int BatchSize { get; set; } = 1000;
}
