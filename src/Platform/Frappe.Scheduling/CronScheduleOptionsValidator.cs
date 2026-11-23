using Microsoft.Extensions.Options;

namespace Frappe.Scheduling;

/// <summary>Fails at startup, naming the full key, when a job's cron is not one Quartz can read (<see cref="CronScheduleOptions"/>).</summary>
[OptionsValidator]
public sealed partial class CronScheduleOptionsValidator : IValidateOptions<CronScheduleOptions>;
