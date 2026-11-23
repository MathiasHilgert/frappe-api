using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Frappe.Http;

/// <summary>Registers the error message catalog and its startup validation, so every error code has its text in each served language.</summary>
public static class ErrorMessageRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the messages embedded in a module's assembly. Modules call it from their HTTP adapter; the platform
        /// messages are registered the first time it is called.
        /// </summary>
        public IServiceCollection AddErrorMessages(Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(assembly);

            if (!services.Any(d => d.ServiceType == typeof(ErrorMessageCatalog)))
            {
                // ValidateOnStart builds the catalog options when the host starts, so a duplicated or untranslated key
                // fails the deployment instead of the first error response in production.
                services.AddOptions<ErrorMessageOptions>().ValidateOnStart()
                    .Configure(options => options.Add(typeof(ErrorMessageCatalog).Assembly));
                services.TryAddSingleton<ErrorMessageCatalog>();
            }

            services.Configure<ErrorMessageOptions>(options => options.Add(assembly));
            return services;
        }
    }
}
