using System.Globalization;
using System.Security.Cryptography;
using Frappe.Identity.Application.Security;

namespace Frappe.Identity.Infrastructure.Security;

internal sealed class RandomSecretGenerator : ISecretGenerator
{
    private const int TokenBytes = 32;

    /// <summary>256 random bits as unpadded base64url (43 characters).</summary>
    public string NewSessionToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Uniform over 000000–999999, leading zeros kept.</summary>
    public string NewVerificationCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
}
