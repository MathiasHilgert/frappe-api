namespace Frappe.Platform;

// The process role; one binary runs any of them.
internal enum HostRole
{
    All,
    Api,
    Worker,
    Scheduler,
}
