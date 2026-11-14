namespace Frappe.Scheduling;

/// <summary>A job's schedule: a Quartz cron expression, seconds first, from <c>{section}:Cron</c>.</summary>
public sealed class CronScheduleOptions
{
    public string? Cron { get; set; }
}
