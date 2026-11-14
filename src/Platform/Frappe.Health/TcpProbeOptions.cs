namespace Frappe.Health;

/// <summary>
/// The ports of the <c>tcpSocket</c> probes, bound from <see cref="Section"/> (<c>Health:Probes:LivePort</c>,
/// <c>ReadyPort</c>, <c>StartupPort</c>), by default those of <see cref="TcpProbePorts.Default"/>.
/// </summary>
public sealed class TcpProbeOptions
{
    /// <summary>The configuration section the hosts bind.</summary>
    public const string Section = "Health:Probes";

    /// <summary>The port open while the process is alive.</summary>
    public int LivePort { get; set; } = TcpProbePorts.Default.Live;

    /// <summary>The port open while every critical dependency answers.</summary>
    public int ReadyPort { get; set; } = TcpProbePorts.Default.Ready;

    /// <summary>The port open once the process has started.</summary>
    public int StartupPort { get; set; } = TcpProbePorts.Default.Startup;
}
