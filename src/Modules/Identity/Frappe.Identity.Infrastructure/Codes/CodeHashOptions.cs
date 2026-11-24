namespace Frappe.Identity.Infrastructure.Codes;

/// <summary>
/// The key one-time codes are hashed with (S5), only from the environment (CFG-10): <c>Identity__Codes__Key</c>. A leaked table of hashes
/// cannot be reversed by trying the million six-digit codes without it. Changing the key makes every live code unredeemable, which costs
/// the people holding one a new request (codes live 15 minutes).
/// </summary>
public sealed class CodeHashOptions
{
    /// <summary>The section the options are bound from.</summary>
    public const string Section = "Identity:Codes";

    /// <summary>The shortest key accepted: the size of the HMAC-SHA256 output.</summary>
    public const int MinKeyBytes = 32;

    /// <summary>
    /// <c>Identity:Codes:Key</c>: base64 of at least 32 bytes (<c>openssl rand -base64 32</c>). Required, no default; the host refuses to
    /// start without it. The Api needs it, the Worker does not.
    /// </summary>
    public string? Key { get; set; }
}
