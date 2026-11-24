using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;

namespace Frappe.Messaging.Storage.Outbox;

/// <summary>Registers the outbox for a module: the interceptor that stages integration events in the same transaction as the data.</summary>
public static class OutboxRegistration
{
    extension<TBuilder>(TBuilder builder)
        where TBuilder : DbContextOptionsBuilder
    {
        /// <summary>
        /// Makes a module context write its integration events to its own outbox. <paramref name="module"/> becomes the
        /// CloudEvents source (<c>/frappe/&lt;module&gt;</c>).
        /// </summary>
        public TBuilder UseOutbox(string module, IIntegrationEventTranslator translator, IClock clock)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(module);
            builder.AddInterceptors(new OutboxInterceptor(translator, $"/frappe/{module}", clock));
            return builder;
        }

        /// <summary>
        /// The same, for the callback of <c>AddModuleDatabase</c>: the translator is the module's
        /// <see cref="IIntegrationEventTranslator"/>, registered with <c>AddKeyedSingleton(module, ...)</c> so that several modules
        /// can share a host, and the clock is the one the platform registers.
        /// </summary>
        public TBuilder UseOutbox(IServiceProvider provider, string module)
        {
            ArgumentNullException.ThrowIfNull(provider);
            return builder.UseOutbox(
                module,
                provider.GetRequiredKeyedService<IIntegrationEventTranslator>(module),
                provider.GetRequiredService<IClock>());
        }
    }
}
