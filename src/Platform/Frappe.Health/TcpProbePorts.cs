namespace Frappe.Health;

/// <summary>The ports a process without HTTP opens for Kubernetes <c>tcpSocket</c> probes.</summary>
public sealed record TcpProbePorts(int Live, int Ready, int Startup)
{
    public static TcpProbePorts Default { get; } = new(8081, 8082, 8083);
}
