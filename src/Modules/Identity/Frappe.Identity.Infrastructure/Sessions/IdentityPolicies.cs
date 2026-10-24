using Frappe.Platform.Web.Security;
using Microsoft.AspNetCore.Builder;

namespace Frappe.Identity.Infrastructure.Sessions;

internal static class IdentityPolicies
{
    /// <summary>An owner acting on their own account through one of their sessions.</summary>
    public const string OwnerSelf = "owner-self";

    /// <summary>The one convention for owner endpoints: <see cref="OwnerSelf"/>, with its 401 and 403 problems documented.</summary>
    public static TBuilder RequireOwner<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequirePolicy(OwnerSelf);
}
