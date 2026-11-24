namespace Frappe.Identity.Application.Owners;

/// <summary>An owner as they see themselves (S10). It holds personal data, so it prints none (DOM-06).</summary>
/// <param name="Id">The owner.</param>
/// <param name="Kind">What the principal is, as a code (<c>owner</c>).</param>
/// <param name="GivenName">The given name.</param>
/// <param name="FamilyName">The family name, if any.</param>
/// <param name="Email">The address as typed.</param>
/// <param name="Locale">The language of the mail and messages the owner receives, as a BCP 47 tag.</param>
/// <param name="Version">Names this state of the owner's row: it changes with every change of the profile (the row's <c>xmin</c>).</param>
public sealed record OwnerProfile(
    Guid Id, string Kind, string GivenName, string? FamilyName, string Email, string Locale, string Version)
{
    public override string ToString() => "OwnerProfile { the content is hidden }";
}
