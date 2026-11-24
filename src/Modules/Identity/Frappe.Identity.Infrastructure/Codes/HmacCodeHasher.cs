using System.Security.Cryptography;
using System.Text;
using Frappe.Identity.Application.Codes;
using Frappe.Identity.Domain.Codes;
using Microsoft.Extensions.Options;

namespace Frappe.Identity.Infrastructure.Codes;

/// <summary>
/// HMAC-SHA256 of the code's text under the configured key (S5, <see cref="CodeHashOptions"/>), compared in fixed time. The key is read and
/// validated once, when the host starts.
/// </summary>
internal sealed class HmacCodeHasher : ICodeHasher
{
    private readonly byte[] _key;

    public HmacCodeHasher(IOptions<CodeHashOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _key = CodeHashOptionsValidator.TryDecode(options.Value.Key ?? string.Empty, out var key) && key.Length >= CodeHashOptions.MinKeyBytes
            ? key
            : throw new InvalidOperationException(
                $"Configuration '{CodeHashOptions.Section}:Key' must be base64 of at least {CodeHashOptions.MinKeyBytes} bytes.");
    }

    public CodeHmac Hash(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        return CodeHmac.Create(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(code))).Value;
    }

    public bool Matches(CodeHmac stored, string candidate)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return !string.IsNullOrEmpty(candidate) && stored.Equals(Hash(candidate));
    }
}
