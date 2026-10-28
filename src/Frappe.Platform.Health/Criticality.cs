namespace Frappe.Platform.Health;

/// <summary>How much the process depends on an external dependency to serve traffic.</summary>
public enum Criticality
{
    /// <summary>
    /// The process cannot serve traffic without it. A failure reports <c>Unhealthy</c>, fails
    /// <c>/health/ready</c> and <c>/health/startup</c> with 503, and takes the replica out of the load balancer.
    /// </summary>
    Critical,

    /// <summary>
    /// The process keeps serving traffic without it, for example a cache. A failure reports
    /// <c>Degraded</c>, never fails a probe, and is not run by <c>/health/ready</c>.
    /// </summary>
    NonCritical,
}
