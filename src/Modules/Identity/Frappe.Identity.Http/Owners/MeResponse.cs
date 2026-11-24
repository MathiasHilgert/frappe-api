using Frappe.Http;

namespace Frappe.Identity.Http.Owners;

/// <summary>The caller's own profile.</summary>
/// <param name="Id">The owner.</param>
/// <param name="Kind">What the principal is (<c>owner</c>), with its name in the request language.</param>
/// <param name="Name">The name, as typed.</param>
/// <param name="Email">The address the owner signs in with.</param>
/// <param name="Locale">
/// The language of the mail and messages the owner receives: its BCP 47 code and its own native name, the same whatever <c>Accept-Language</c>
/// says.
/// </param>
public sealed record MeResponse(Guid Id, EnumeratedValue Kind, OwnerNameResponse Name, string Email, EnumeratedValue Locale)
{
    /// <summary>Masked: the profile is personal data (DOM-06).</summary>
    public override string ToString() => "MeResponse { the content is hidden }";
}
