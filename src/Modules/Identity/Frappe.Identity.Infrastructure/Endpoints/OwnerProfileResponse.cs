using Frappe.Identity.Application.Sessions;

namespace Frappe.Identity.Infrastructure.Endpoints;

/// <summary>GET /me: the signed-in owner's own profile, as the HTTP contract shows it.</summary>
internal sealed record OwnerProfileResponse(Guid Id, string Name, string Email, string Status, string Locale)
{
    public static OwnerProfileResponse From(OwnerProfile profile) =>
        new(profile.Id, profile.Name, profile.Email, profile.Status, profile.Locale);
}
