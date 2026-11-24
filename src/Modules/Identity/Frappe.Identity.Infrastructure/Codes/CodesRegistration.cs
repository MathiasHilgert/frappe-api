using Frappe.Identity.Application.Codes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Frappe.Identity.Infrastructure.Codes;

/// <summary>Registers how Identity issues and redeems one-time codes.</summary>
internal static class CodesRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the code hasher, the repository and the two services over them, and fails at startup, naming the key, when
        /// <c>Identity:Codes:Key</c> is missing or too short (CFG-01). The services need an <c>IAttemptLimiter</c>, which a plug-in provides.
        /// </summary>
        public IServiceCollection AddCodes()
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton(TimeProvider.System);
            services.AddOptions<CodeHashOptions>().BindConfiguration(CodeHashOptions.Section).ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<CodeHashOptions>, CodeHashOptionsValidator>());
            services.TryAddSingleton<ICodeHasher, HmacCodeHasher>();
            services.TryAddScoped<IOneTimeCodes, EfOneTimeCodes>();
            services.TryAddScoped<CodeIssuer>();
            services.TryAddScoped<CodeRedeemer>();
            return services;
        }
    }
}
