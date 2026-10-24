using Quartz;

namespace Frappe.Platform.Messaging.Jobs;

/// <summary>A recurring job a module declares: the durable job and its cron schedule. The trigger takes the job's name.</summary>
public sealed record RecurringJob(IJobDetail Job, string CronExpression, TimeZoneInfo TimeZone);
