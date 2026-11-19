using System.Net;

namespace Frappe.Http;

/// <summary>
/// The proxies trusted to send <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c>: addresses in
/// <c>Http:ForwardedHeaders:KnownProxies</c> and networks in <c>KnownNetworks</c>, comma separated, validated at startup
/// by <see cref="TrustedProxyOptionsValidator"/>.
/// </summary>
public sealed class TrustedProxyOptions
{
    /// <summary>The configuration section the hosts bind.</summary>
    public const string Section = "Http:ForwardedHeaders";

    /// <summary>
    /// Addresses of the proxies whose <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> are believed, as IPv4 or IPv6
    /// literals separated by commas (for example <c>10.0.0.5, 10.0.0.6</c>). Unit: IP addresses. Default: <see langword="null"/>,
    /// no proxy is trusted by address.
    /// </summary>
    public string? KnownProxies { get; set; }

    /// <summary>
    /// Networks whose proxies are believed, in CIDR notation separated by commas (for example <c>10.0.0.0/8</c>). Unit:
    /// networks with a prefix length in bits. Default: <see langword="null"/>, no network is trusted.
    /// </summary>
    public string? KnownNetworks { get; set; }

    /// <summary>The trusted proxies, parsed.</summary>
    public IPAddress[] Proxies() => [.. Entries(KnownProxies).Select(IPAddress.Parse)];

    /// <summary>The trusted networks, parsed.</summary>
    public IPNetwork[] Networks() => [.. Entries(KnownNetworks).Select(IPNetwork.Parse)];

    /// <summary>The non-empty, trimmed entries of a comma separated list.</summary>
    public static string[] Entries(string? list) =>
        (list ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
