using Frappe.Identity.Http.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Identity.Http.Owners;

/// <summary>The policy of the routes only an owner may use: a session of a principal whose kind is <c>owner</c>.</summary>
internal static class OwnerPolicy
{
    extension<TBuilder>(TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        /// <summary>Requires a session whose principal is an owner (<see cref="Name"/>); another kind's session is refused with 403.</summary>
        public TBuilder RequireOwner()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.RequireAuthorization(Name);
            return builder;
        }
    }

    extension(IServiceCollection services)
    {
        /// <summary>Adds <see cref="Name"/>: the session scheme, an authenticated user and the claim <c>kind = owner</c>.</summary>
        public IServiceCollection AddOwnerPolicy()
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AddAuthorizationBuilder().AddPolicy(
                Name,
                policy => policy.AddAuthenticationSchemes(SessionAuthentication.Scheme)
                    .RequireAuthenticatedUser()
                    .RequireClaim(SessionClaims.KindClaim, SessionClaims.OwnerKind));
            return services;
        }
    }

    /// <summary>The name of the authorization policy.</summary>
    public const string Name = "Identity:Owner";
}
