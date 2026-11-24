using Frappe.Identity.Application.Limits;
using Frappe.Identity.Application.Owners.LogInOwner;

namespace Frappe.Identity.Application.Owners.RegisterOwner;

/// <summary>The limit on the notice mails a registration causes (S3): the flow <see cref="RegisterOwnerHandler"/> counts under.</summary>
public static class RegistrationLimits
{
    /// <summary>
    /// Notices to one verified owner's address: 3 per hour, however many registrations try it and from wherever, so nobody can use Frappé to
    /// flood a mailbox. Fails open (SEC-14): a notice dropped is harmless, and the answer of the registration never changes.
    /// </summary>
    public static readonly LimitFlow Notice = new("registration.notice", 3, TimeSpan.FromHours(1), LimitFailure.FailOpen);

    /// <summary>The subject notices to <paramref name="normalizedAddress"/> are counted under: a hash, never the address.</summary>
    internal static string Subject(string normalizedAddress) => LogInLimits.Subject(normalizedAddress);
}
