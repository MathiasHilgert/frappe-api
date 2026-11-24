namespace Frappe.Identity.Application.Sessions;

/// <summary>A new session token: its text, shown to the holder once, and the hash that is stored in its place.</summary>
/// <param name="Value">The token as the client sends it.</param>
/// <param name="Hash">The SHA-256 of the token's random bytes; the only form that is stored.</param>
public sealed record IssuedToken(string Value, byte[] Hash)
{
    /// <summary>Masked: a token is a secret that appears only in the log-in answer and the cookie (SEC-07, DOM-06).</summary>
    public override string ToString() => "IssuedToken { Value = *** }";
}
