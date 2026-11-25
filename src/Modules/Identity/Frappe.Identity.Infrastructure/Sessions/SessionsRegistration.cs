using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Frappe.Identity.Infrastructure.Sessions;

/// <summary>Registers how Identity opens, reads and closes sessions.</summary>
internal static class SessionsRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the token format, the repository, the read port, the opener, what a credential change does to sessions and the password
        /// recheck, and the lifetimes from <c>Identity:Sessions</c>, validated when the
        /// host starts, naming the key (CFG-01). Both lifetimes have defaults, so no key is required.
        /// </summary>
        public IServiceCollection AddSessions()
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton(TimeProvider.System);
            services.AddOptions<SessionOptions>().BindConfiguration(SessionOptions.Section).ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SessionOptions>, SessionOptionsValidator>());
            services.TryAddSingleton(provider =>
            {
                var options = provider.GetRequiredService<IOptions<SessionOptions>>().Value;
                return new SessionLifetimes(Duration.FromTimeSpan(options.IdleTimeout), Duration.FromTimeSpan(options.AbsoluteTimeout));
            });
            services.TryAddSingleton<ISessionTokens, SessionTokens>();
            services.TryAddScoped<ISessions, EfSessions>();
            services.TryAddScoped<ISessionReads, EfSessionReads>();
            services.TryAddScoped<SessionOpener>();
            services.TryAddScoped<CredentialChange>();
            services.TryAddScoped<PasswordRecheck>();
            return services;
        }
    }
}
