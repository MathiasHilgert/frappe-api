using System.Net;

namespace Frappe.Api;

/// <summary>
/// The proxies trusted to send <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c>: addresses in
/// <c>Http:ForwardedHeaders:KnownProxies</c> and networks in <c>KnownNetworks</c>, comma separated, validated at startup
/// by <see cref="TrustedProxyOptionsValidator"/>.
/// </summary>
public sealed class TrustedProxyOptions
{
    /// <summary>The configuration section the Api binds.</summary>
    public const string Section = "Http:ForwardedHeaders";

    /// <summary>Trusted proxy addresses, comma separated.</summary>
    public string? KnownProxies { get; set; }

    /// <summary>Trusted networks in CIDR notation, comma separated.</summary>
    public string? KnownNetworks { get; set; }

    /// <summary>The trusted proxies, parsed.</summary>
    public IPAddress[] Proxies() => Entries(KnownProxies).Select(IPAddress.Parse).ToArray();

    /// <summary>The trusted networks, parsed.</summary>
    public IPNetwork[] Networks() => Entries(KnownNetworks).Select(IPNetwork.Parse).ToArray();

    /// <summary>The non-empty, trimmed entries of a comma separated list.</summary>
    public static string[] Entries(string? list) =>
        (list ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
