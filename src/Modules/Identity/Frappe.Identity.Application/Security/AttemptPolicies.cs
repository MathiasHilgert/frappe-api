namespace Frappe.Identity.Application.Security;

/// <summary>The identity limits (S5, S6). Wider per-IP limits tolerate shared networks such as a restaurant's Wi-Fi.</summary>
public static class AttemptPolicies
{
    public static readonly AttemptPolicy LoginFailuresPerEmail = new("login-email", 10, TimeSpan.FromMinutes(15));
    public static readonly AttemptPolicy LoginFailuresPerIp = new("login-ip", 100, TimeSpan.FromMinutes(15));
    public static readonly AttemptPolicy RegisterPerEmail = new("register-email", 5, TimeSpan.FromHours(1));
    public static readonly AttemptPolicy RegisterPerIp = new("register-ip", 20, TimeSpan.FromHours(1));

    /// <summary>Wrong email codes per owner: six digits must not be guessable, yet a typo costs only a few minutes.</summary>
    public static readonly AttemptPolicy VerifyEmailPerOwner = new("verify-email-owner", 5, TimeSpan.FromMinutes(15));
}
