namespace Frappe.Identity.Application.Sessions;

/// <summary>Hands the web console its session token in a cookie, and takes it back on sign-out.</summary>
public interface ISessionCookie
{
    void Issue(string token, DateTimeOffset expiresAt);

    void Clear();
}
