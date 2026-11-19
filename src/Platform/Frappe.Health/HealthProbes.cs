namespace Frappe.Health;

/// <summary>
/// The three Kubernetes probes, as health check tags. Liveness checks only the process: a dependency outage must
/// never restart every replica at once. Readiness is the sum of the critical dependencies: one down takes the replica
/// out of traffic without restarting it. Startup says the host finished starting.
/// </summary>
public static class HealthProbes
{
    public const string Live = "live";
    public const string Ready = "ready";
    public const string Startup = "startup";
}
