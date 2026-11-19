using System.Net.Sockets;

namespace Frappe.Health;

/// <summary>
/// <c>&lt;host binary&gt; --probe &lt;target&gt;</c>: checks a probe from inside the container and exits 0 when it is
/// healthy, 1 otherwise. Container healthchecks use it because the chiseled image has no shell, curl or nc.
/// The target is <c>http://localhost:8080/health/ready</c> (a 2xx answer) or <c>tcp://localhost:8082</c> (an open port).
/// </summary>
public static class ProbeCommand
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    /// <summary>Whether the process was started as a probe rather than as the host.</summary>
    public static bool IsProbe(string[] args, out string target)
    {
        ArgumentNullException.ThrowIfNull(args);
        target = args is ["--probe", var value] ? value : string.Empty;
        return target.Length > 0;
    }

    /// <summary>Runs the probe of <paramref name="target"/>, writing why a target is malformed to the standard error.</summary>
    /// <returns>0 when the probe is healthy, 1 otherwise.</returns>
    public static Task<int> RunAsync(string target) => RunAsync(target, Console.Error);

    /// <summary>Runs the probe of <paramref name="target"/>, writing why a target is malformed to <paramref name="error"/>.</summary>
    /// <returns>0 when the probe is healthy, 1 otherwise.</returns>
    public static async Task<int> RunAsync(string target, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || !IsProbeTarget(uri))
        {
            await error.WriteLineAsync($"'{target}' is not a probe target (http://host:port/path or tcp://host:port).");
            return 1;
        }

        using var timeout = new CancellationTokenSource(Timeout);
        try
        {
            if (uri.Scheme == "tcp")
            {
                using var client = new TcpClient();
                await client.ConnectAsync(uri.Host, uri.Port, timeout.Token);
                return 0;
            }

            using var http = new HttpClient { Timeout = Timeout };
            using var response = await http.GetAsync(uri, timeout.Token);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception exception) when (exception is HttpRequestException or SocketException or OperationCanceledException)
        {
            return 1;
        }
    }

    /// <summary>An <c>http</c> or <c>https</c> address, or a <c>tcp</c> one that names its port.</summary>
    private static bool IsProbeTarget(Uri uri) => uri.Scheme switch
    {
        "tcp" => uri.Port is >= 1 and <= 65535,
        "http" or "https" => true,
        _ => false,
    };
}
