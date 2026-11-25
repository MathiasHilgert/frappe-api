using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Identifiers;
using Frappe.Identity.Application.Owners;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Infrastructure.Credentials;
using Frappe.Identity.Infrastructure.Identifiers;
using Frappe.Identity.Infrastructure.Principals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Frappe.Identity.Infrastructure.Owners;

/// <summary>Registers the repositories Owners use cases stage through.</summary>
internal static class OwnersRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the repositories of principals, addresses, passwords and owners.
        /// </summary>
        public IServiceCollection AddOwners()
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddScoped<IPrincipals, EfPrincipals>();
            services.TryAddScoped<IEmailIdentifiers, EfEmailIdentifiers>();
            services.TryAddScoped<IPasswordCredentials, EfPasswordCredentials>();
            services.TryAddScoped<IOwners, EfOwners>();
            services.TryAddScoped<AddressClaim>();
            services.TryAddScoped<IOwnerReads, EfOwnerReads>();
            return services;
        }
    }
}
