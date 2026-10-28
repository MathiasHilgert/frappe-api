namespace Frappe.Platform.Hosting;

/// <summary>The part of the system a process runs; one binary runs any of them.</summary>
public enum HostRole
{
    /// <summary>Every part in one process: business endpoints, message handlers and schedules.</summary>
    All,

    /// <summary>Business endpoints only; registers no job, projection or scheduler listener.</summary>
    Api,

    /// <summary>Message handlers only; maps no business endpoint.</summary>
    Worker,

    /// <summary>Recurring schedules only; maps no business endpoint.</summary>
    Scheduler,
}
