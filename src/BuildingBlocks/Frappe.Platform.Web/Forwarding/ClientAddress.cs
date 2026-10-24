using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;

namespace Frappe.Platform.Web.Forwarding;

public static class ClientAddress
{
    private const int Ipv6SubscriberPrefixBytes = 8;

    /// <summary>
    /// The caller's address as a rate-limit key, or <c>unknown</c> when the transport has none. Already the forwarded
    /// address when the request came through a trusted proxy. IPv4 is the address itself; IPv6 is its /64 prefix
    /// (<c>2001:db8:1:2::/64</c>), because one subscriber is routinely handed a whole /64 and could otherwise rotate
    /// through billions of addresses to dodge every per-IP limit. An IPv4-mapped IPv6 address is the IPv4 one.
    /// </summary>
    public static string ClientIp(this HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return Key(http.Connection.RemoteIpAddress);
    }

    private static string Key(IPAddress? address)
    {
        if (address is null)
            return "unknown";
        if (address.IsIPv4MappedToIPv6)
            return address.MapToIPv4().ToString();
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();

        Span<byte> bytes = stackalloc byte[16];
        address.TryWriteBytes(bytes, out _);
        bytes[Ipv6SubscriberPrefixBytes..].Clear();
        return $"{new IPAddress(bytes)}/{Ipv6SubscriberPrefixBytes * 8}";
    }
}
