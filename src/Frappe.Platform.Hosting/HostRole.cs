namespace Frappe.Platform.Hosting;

// The process role; one binary runs any of them.
public enum HostRole
{
    All,
    Api,
    Worker,
    Scheduler,
}
