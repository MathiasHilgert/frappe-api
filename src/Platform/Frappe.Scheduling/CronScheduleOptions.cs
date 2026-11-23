using System.ComponentModel.DataAnnotations;
using Quartz;

namespace Frappe.Scheduling;

/// <summary>A job's schedule: a Quartz cron expression, seconds first, from <c>{section}:Cron</c>.</summary>
public sealed class CronScheduleOptions : IValidatableObject
{
    /// <summary>
    /// <c>{section}:Cron</c> (for example <c>Orders:MessagePurge:Cron</c>): the job's Quartz cron expression, seconds
    /// first. Default the job's own default passed to <c>AddCronJob</c>; unset or empty means that default. Range
    /// any expression Quartz can parse; an invalid one fails at startup. Changing it moves every replica's next
    /// run, since one clustered schedule exists per job.
    /// </summary>
    public string? Cron { get; set; }

    // The key shows the job's own section, which only the registration knows: a data annotation cannot carry it.
    internal string ConfigurationSection { get; set; } = string.Empty;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Cron is null || !CronExpression.TryParse(Cron, out _))
        {
            yield return new ValidationResult(
                $"Configuration '{ConfigurationSection}:{nameof(Cron)}' must be a Quartz cron expression, seconds first.",
                [nameof(Cron)]);
        }
    }
}
