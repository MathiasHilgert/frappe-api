using Frappe.Identity.Domain.Credentials;

namespace Frappe.Identity.Application.Credentials;

/// <summary>Asks a public breach list whether a password appears in it (DES-03: the vendor is a strategy).</summary>
public interface IBreachedPasswords
{
    /// <summary>
    /// <see langword="true"/> when <paramref name="password"/> is listed, <see langword="false"/> when it is not, and <see langword="null"/>
    /// when the list could not answer (down, slow, refused): the caller accepts the password then (S4). Never throws, except for the
    /// caller's own cancellation.
    /// </summary>
    Task<bool?> IsBreachedAsync(Password password, CancellationToken cancellationToken);
}
