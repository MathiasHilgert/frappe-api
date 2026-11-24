using System.Security.Cryptography;
using System.Text;
using Frappe.Identity.Application.Limits;

namespace Frappe.Identity.Application.Owners.LogInOwner;

/// <summary>The limit on log-in attempts (S7): the flow <see cref="LogInOwnerHandler"/> counts under.</summary>
public static class LogInLimits
{
    /// <summary>The platform's code for a throttled caller (<c>http.rate_limited</c>): its text is the platform's.</summary>
    public const string ThrottledCode = "http.rate_limited";

    /// <summary>
    /// Attempts at one address: 10 per 15 minutes, whatever their outcome, so a guessed password costs the attacker time and the owner is
    /// never locked out for longer than the window. Fails closed (SEC-14): it guards a password, so while attempts cannot be counted none is judged.
    /// </summary>
    public static readonly LimitFlow Attempt = new("login.attempt", 10, TimeSpan.FromMinutes(15), LimitFailure.FailClosed);

    private const int LongestSubject = 256;

    /// <summary>
    /// The subject attempts at <paramref name="typedAddress"/> are counted under: a hash of the normalized text, never the address (the limiter
    /// forbids it) and the same for an address that exists and one that does not, so the limit reveals nothing.
    /// </summary>
    internal static string Subject(string? typedAddress)
    {
        var text = (typedAddress ?? string.Empty).Trim().ToLowerInvariant();
        var bounded = text.Length > LongestSubject ? text[..LongestSubject] : text;
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(bounded)));
    }
}
