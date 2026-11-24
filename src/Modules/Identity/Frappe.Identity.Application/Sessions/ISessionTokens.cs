namespace Frappe.Identity.Application.Sessions;

/// <summary>
/// Makes session tokens and finds the hash a presented one is stored under (DES-03: the format is a strategy, and its prefix tells later
/// schemes apart). A token is opaque: it says nothing about its session and only its hash is kept.
/// </summary>
public interface ISessionTokens
{
    /// <summary>A new token from a cryptographic random source, with the hash to store.</summary>
    IssuedToken Issue();

    /// <summary>
    /// The hash of <paramref name="token"/> as presented by a client, or <see langword="null"/> when it is not a token of this format (wrong
    /// prefix, not base64url, not the exact length, not written the way it is issued), so nothing needs to be looked up for it.
    /// </summary>
    byte[]? HashOf(string? token);
}
