namespace Frappe.Identity.Infrastructure.Sessions;

/// <summary>Where the web console keeps its session token. Always HttpOnly, Secure and SameSite=Lax.</summary>
public sealed class SessionCookieOptions
{
    public const string Section = "Identity:SessionCookie";

    /// <summary>
    /// Browsers only accept a <c>__Host-</c> cookie that is Secure, has Path=/ and no Domain, so a sibling subdomain
    /// or a plain-HTTP page cannot plant a cookie of the same name to shadow the real one.
    /// </summary>
    public const string HostPrefix = "__Host-";

    public string Name { get; set; } = HostPrefix + "frappe_session";

    public string Path { get; set; } = "/";

    /// <summary>Only for names without the <see cref="HostPrefix"/>; left unset, the cookie is host-only.</summary>
    public string? Domain { get; set; }

    // A cookie name is an RFC 6265 token: no separators, spaces or control characters.
    public bool IsValid() =>
        !string.IsNullOrEmpty(Name)
        && Name.All(c => c > ' ' && c < 127 && !"()<>@,;:\\\"/[]?={}".Contains(c, StringComparison.Ordinal))
        && Path.StartsWith('/')
        && (!Name.StartsWith(HostPrefix, StringComparison.Ordinal) || (Path == "/" && string.IsNullOrEmpty(Domain)));
}
