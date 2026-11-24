using Frappe.Identity.Domain.Credentials;

namespace Frappe.Identity.Application.Credentials;

/// <summary>
/// Turns passwords into stored hashes and back into verdicts (DES-03: the algorithm is a strategy). The hash carries its own algorithm,
/// parameters and salt, so a hash made under old parameters still verifies until <see cref="NeedsRehash"/> says to replace it.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>The hash of <paramref name="password"/> under the current parameters and a fresh salt.</summary>
    PasswordHash Hash(Password password);

    /// <summary>
    /// Whether <paramref name="candidate"/> is the password <paramref name="hash"/> was made from. Takes the raw text so a wrong attempt of any
    /// length is a plain <see langword="false"/>; a malformed hash is <see langword="false"/> too, never an exception.
    /// </summary>
    bool Verify(PasswordHash hash, string candidate);

    /// <summary>Whether <paramref name="hash"/> was made under other parameters than the current ones, so the next log-in replaces it.</summary>
    bool NeedsRehash(PasswordHash hash);
}
