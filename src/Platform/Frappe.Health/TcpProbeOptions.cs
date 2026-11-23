using System.ComponentModel.DataAnnotations;

namespace Frappe.Health;

/// <summary>
/// The ports of the <c>tcpSocket</c> probes, bound from <see cref="Section"/> (<c>Health:Probes:LivePort</c>,
/// <c>ReadyPort</c>, <c>StartupPort</c>), by default those of <see cref="TcpProbePorts.Default"/>.
/// </summary>
public sealed class TcpProbeOptions : IValidatableObject
{
    /// <summary>The configuration section the hosts bind.</summary>
    public const string Section = "Health:Probes";

    /// <summary>
    /// <c>Health:Probes:LivePort</c>: the TCP port that is open while the process is alive (the Kubernetes liveness
    /// probe). Default 8081. Range 1 to 65535, and not used by anything else in the container. Changing it moves
    /// the probe: the deployment's <c>tcpSocket</c> liveness probe must name the same port, or the kubelet reads a
    /// closed port as dead and restarts the pod.
    /// </summary>
    [Port("Health:Probes:LivePort")]
    public int LivePort { get; set; } = TcpProbePorts.Default.Live;

    /// <summary>
    /// <c>Health:Probes:ReadyPort</c>: the TCP port that is open while every critical dependency answers (the
    /// readiness probe). Default 8082. Range 1 to 65535, distinct from the other probe ports. Changing it moves the
    /// probe: the deployment's readiness probe must name the same port, or the pod never receives traffic.
    /// </summary>
    [Port("Health:Probes:ReadyPort")]
    public int ReadyPort { get; set; } = TcpProbePorts.Default.Ready;

    /// <summary>
    /// <c>Health:Probes:StartupPort</c>: the TCP port that is open once the process has started (the startup
    /// probe). Default 8083. Range 1 to 65535, distinct from the other probe ports. Changing it moves the probe:
    /// the deployment's startup probe must name the same port, or the pod is killed before it starts.
    /// </summary>
    [Port("Health:Probes:StartupPort")]
    public int StartupPort { get; set; } = TcpProbePorts.Default.Startup;

    /// <summary>Reports every pair of probe ports that share a number, naming both full keys.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        (string Key, int Port)[] probes =
        [
            ("Health:Probes:LivePort", LivePort),
            ("Health:Probes:ReadyPort", ReadyPort),
            ("Health:Probes:StartupPort", StartupPort),
        ];
        for (var first = 0; first < probes.Length; first++)
        {
            for (var second = first + 1; second < probes.Length; second++)
            {
                if (probes[first].Port == probes[second].Port)
                {
                    yield return new ValidationResult(
                        $"Configuration '{probes[first].Key}' and '{probes[second].Key}' must be distinct ports, both are {probes[first].Port}.");
                }
            }
        }
    }
}
