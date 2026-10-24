namespace Frappe.Identity.Application.Security;

/// <summary>The identity limits. Wider per-IP limits tolerate shared networks such as a restaurant's Wi-Fi.</summary>
public static class AttemptPolicies
{
    /// <summary>
    /// Wrong passwords for one email from one IP: the tight budget. Keyed on both, so an attacker hammering an email
    /// from their own address throttles only themselves and never locks the owner out.
    /// </summary>
    public static readonly AttemptPolicy LoginFailuresPerEmailAndIp = new("login-email-ip", 10, TimeSpan.FromMinutes(15));

    /// <summary>Wrong passwords for one email from anywhere: a much higher ceiling that only a distributed attack reaches.</summary>
    public static readonly AttemptPolicy LoginFailuresPerEmail = new("login-email", 100, TimeSpan.FromMinutes(15));
    public static readonly AttemptPolicy LoginFailuresPerIp = new("login-ip", 100, TimeSpan.FromMinutes(15));
    public static readonly AttemptPolicy RegisterPerEmail = new("register-email", 5, TimeSpan.FromHours(1));
    /// <summary>Wrong passwords typed inside a session (step-up, change password), shared so neither doubles the budget.</summary>
    public static readonly AttemptPolicy ReverifyFailuresPerPrincipal = new("reverify-principal", 5, TimeSpan.FromMinutes(15));
    /// <summary>Every email change request counts: each one sends a mail to an address the owner chose.</summary>
    public static readonly AttemptPolicy EmailChangeRequestsPerPrincipal = new("email-change-principal", 5, TimeSpan.FromHours(1));
    /// <summary>Wrong email change codes; six digits must not be guessable inside a code's 15-minute life.</summary>
    public static readonly AttemptPolicy EmailChangeConfirmFailuresPerPrincipal = new("email-change-confirm-principal", 5, TimeSpan.FromMinutes(15));
    public static readonly AttemptPolicy RegisterPerIp = new("register-ip", 20, TimeSpan.FromHours(1));

    /// <summary>Wrong email codes per owner: six digits must not be guessable, yet a typo costs only a few minutes.</summary>
    public static readonly AttemptPolicy VerifyEmailPerOwner = new("verify-email-owner", 5, TimeSpan.FromMinutes(15));

    public static readonly AttemptPolicy PasswordResetPerEmail = new("password-reset-email", 5, TimeSpan.FromHours(1));
    public static readonly AttemptPolicy PasswordResetPerIp = new("password-reset-ip", 20, TimeSpan.FromHours(1));

    /// <summary>Wrong codes per reset: five guesses of a six-digit code, then the owner must request a new one.</summary>
    public static readonly AttemptPolicy PasswordResetConfirmFailuresPerReset =
        new("password-reset-confirm-reset", 5, TimeSpan.FromMinutes(15));

    public static readonly AttemptPolicy PasswordResetConfirmFailuresPerIp =
        new("password-reset-confirm-ip", 100, TimeSpan.FromMinutes(15));
}
