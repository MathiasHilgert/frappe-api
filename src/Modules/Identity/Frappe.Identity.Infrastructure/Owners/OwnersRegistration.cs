using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Identifiers;
using Frappe.Identity.Application.Owners;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Owners;
using Frappe.Identity.Infrastructure.Credentials;
using Frappe.Identity.Infrastructure.Identifiers;
using Frappe.Identity.Infrastructure.Principals;
using Frappe.Observability;
using Frappe.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Frappe.Identity.Infrastructure.Owners;

/// <summary>Registers the repositories Owners use cases stage through, and what is counted once their unit commits.</summary>
internal static class OwnersRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the repositories of principals, addresses, passwords and owners, and counts registrations, codes issued and verifications after
        /// the commit that carried them (OBS-06). A failed verification is counted by its handler: nothing commits to count it.
        /// </summary>
        public IServiceCollection AddOwners()
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddScoped<IPrincipals, EfPrincipals>();
            services.TryAddScoped<IEmailIdentifiers, EfEmailIdentifiers>();
            services.TryAddScoped<IPasswordCredentials, EfPasswordCredentials>();
            services.TryAddScoped<IOwners, EfOwners>();
            services.CountOnCommit<OwnerRegistered, IdentityMetrics>((metrics, registered) =>
                metrics.Registered(registered.Replaced ? RegistrationOutcome.Replaced : RegistrationOutcome.Created));
            services.CountOnCommit<OwnerRegistrationAttempted, IdentityMetrics>((metrics, _) => metrics.Registered(RegistrationOutcome.Taken));
            services.CountOnCommit<OwnerCodeIssued, IdentityMetrics>((metrics, issued) =>
                metrics.CodeIssued(StoredEnum<CodePurpose>.ToText(issued.Purpose)));
            services.CountOnCommit<OwnerVerified, IdentityMetrics>((metrics, _) => metrics.Verified(VerificationOutcome.Verified));
            return services;
        }
    }
}
