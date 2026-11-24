using System.Net;
using System.Security.Cryptography;
using System.Text;
using Frappe.Identity.Application.Limits;
using Frappe.Identity.Application.Sessions;

namespace Frappe.Identity.Application.Owners.LogInOwner;

/// <summary>The limit on log-in attempts (S7): the flow <see cref="LogInOwnerHandler"/> counts under.</summary>
public static class LogInLimits
{
    /// <summary>The platform's code for a throttled caller (<c>http.rate_limited</c>): its text is the platform's.</summary>
    public const string ThrottledCode = "http.rate_limited";

    /// <summary>
    /// Attempts at one address from one network prefix (IPv4 /24, IPv6 /48): 10 per 15 minutes, whatever their outcome, so a guessed password
    /// costs the attacker time and one source can never lock the owner out of another network. Fails closed (SEC-14): it guards a password.
    /// </summary>
    public static readonly LimitFlow Attempt = new("login.attempt", 10, TimeSpan.FromMinutes(15), LimitFailure.FailClosed);

    /// <summary>The wide cap on one address from every network together: 100 per hour, so rotating sources still meet a ceiling.</summary>
    public static readonly LimitFlow AttemptByEmail = new("login.attempt.email", 100, TimeSpan.FromHours(1), LimitFailure.FailClosed);

    private const int LongestSubject = 256;

    /// <summary>
    /// The subject attempts at <paramref name="typedAddress"/> are counted under: a hash of the normalized text, never the address (the limiter
    /// forbids it) and the same for an address that exists and one that does not, so the limit reveals nothing.
    /// </summary>
    internal static string Subject(string? typedAddress) => Hash(Bounded(typedAddress));

    /// <summary>The subject for <paramref name="typedAddress"/> typed from <paramref name="client"/>'s network prefix.</summary>
    internal static string Subject(string? typedAddress, IPAddress? client) =>
        Hash(Bounded(typedAddress) + "|" + (ClientNetwork.PrefixOf(client)?.ToString() ?? "unknown"));

    private static string Bounded(string? typedAddress)
    {
        var text = (typedAddress ?? string.Empty).Trim().ToLowerInvariant();
        return text.Length > LongestSubject ? text[..LongestSubject] : text;
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
