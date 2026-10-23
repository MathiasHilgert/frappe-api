namespace Frappe.Identity.Infrastructure.Sessions;

/// <summary>Where the web console keeps its session token. Always HttpOnly, Secure and SameSite=Lax.</summary>
public sealed class SessionCookieOptions
{
    public const string Section = "Identity:SessionCookie";

    public string Name { get; set; } = "frappe_session";

    public string Path { get; set; } = "/";

    // A cookie name is an RFC 6265 token: no separators, spaces or control characters.
    public bool IsValid() =>
        !string.IsNullOrEmpty(Name)
        && Name.All(c => c > ' ' && c < 127 && !"()<>@,;:\\\"/[]?={}".Contains(c, StringComparison.Ordinal))
        && Path.StartsWith('/');
}
