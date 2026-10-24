namespace Frappe.Identity.Application.Security;

/// <summary>
/// Remembers the client addresses (rate-limit keys: an IPv4 address or an IPv6 /64) an email has signed in from with
/// the right password, for <see cref="KnownFor"/> after the last success. A known origin is exempt from the per-email
/// ceiling, so a distributed attack that exhausts it cannot lock the owner out of the places they already use.
/// </summary>
public interface IKnownSignInOrigins
{
    /// <summary>How long a successful sign-in keeps its origin known; every new success extends it.</summary>
    static readonly TimeSpan KnownFor = TimeSpan.FromDays(30);

    /// <summary>Whether <paramref name="email"/> signed in from <paramref name="clientIp"/>; false when unsure.</summary>
    Task<bool> IsKnownAsync(string email, string clientIp, CancellationToken cancellationToken);

    /// <summary>Records a successful sign-in of <paramref name="email"/> from <paramref name="clientIp"/>.</summary>
    Task RememberAsync(string email, string clientIp, CancellationToken cancellationToken);
}
