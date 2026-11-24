using System.Net;
using System.Net.Sockets;

namespace Frappe.Identity.Application.Sessions;

/// <summary>Cuts a client address down to the only part of it a session keeps ("También IP aproximada").</summary>
public static class ClientNetwork
{
    private const int Ipv4PrefixLength = 24;

    private const int Ipv6PrefixLength = 48;

    /// <summary>
    /// The IPv4 /24 or IPv6 /48 that holds <paramref name="address"/> (an IPv4 address in IPv6 form counts as IPv4), or <see langword="null"/>
    /// for none or an address of another family.
    /// </summary>
    public static IPNetwork? PrefixOf(IPAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var length = address.AddressFamily switch
        {
            AddressFamily.InterNetwork => Ipv4PrefixLength,
            AddressFamily.InterNetworkV6 => Ipv6PrefixLength,
            _ => 0,
        };
        if (length == 0)
        {
            return null;
        }

        var bytes = address.GetAddressBytes();
        for (var index = length / 8; index < bytes.Length; index++)
        {
            bytes[index] = 0;
        }

        return new IPNetwork(new IPAddress(bytes), length);
    }
}
