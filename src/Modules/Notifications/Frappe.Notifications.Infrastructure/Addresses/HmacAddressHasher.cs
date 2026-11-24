using System.Security.Cryptography;
using System.Text;
using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;
using Microsoft.Extensions.Options;

namespace Frappe.Notifications.Infrastructure.Addresses;

/// <summary>
/// HMAC-SHA256 of the normalized address (trimmed, lower case, Unicode NFC) under a key from the environment, so a leaked table reveals
/// no address and a dictionary of addresses cannot be tried without the key (S13).
/// </summary>
public sealed class HmacAddressHasher : IAddressHasher
{
    private readonly short _currentKeyId;
    private readonly (short Id, byte[] Key)[] _keys;

    public HmacAddressHasher(IOptions<AddressHashOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;
        _currentKeyId = value.CurrentKeyId;
        _keys = [.. value.Keys.Select(pair => (pair.Key, Convert.FromBase64String(pair.Value)))];
    }

    public AddressHash Hash(string address) => HashWith(_keys.Single(key => key.Id == _currentKeyId), address);

    public IReadOnlyList<AddressHash> HashWithEveryKey(string address) => [.. _keys.Select(key => HashWith(key, address))];

    private static AddressHash HashWith((short Id, byte[] Key) key, string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        var normalized = address.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormC);
        return new AddressHash(HMACSHA256.HashData(key.Key, Encoding.UTF8.GetBytes(normalized)), key.Id);
    }
}
