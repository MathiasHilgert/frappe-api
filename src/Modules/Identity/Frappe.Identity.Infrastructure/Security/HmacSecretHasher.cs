using System.Security.Cryptography;
using System.Text;
using Frappe.Identity.Application.Security;
using Microsoft.Extensions.Options;

namespace Frappe.Identity.Infrastructure.Security;

/// <summary>
/// HMAC-SHA256 with a server-side key, as lowercase hex. Deterministic so a token can be found by its hash; keyed
/// so a leaked table cannot be brute-forced offline (a six-digit code has only a million values).
/// </summary>
internal sealed class HmacSecretHasher(IOptions<SecretHashingOptions> options) : ISecretHasher
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(options.Value.Key ?? string.Empty);

    public string Hash(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        return Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(secret)));
    }
}
