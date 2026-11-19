using Frappe.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Frappe.Http;

/// <summary>
/// Wires where the answers of requests sent with an <c>Idempotency-Key</c> are kept (<see cref="IIdempotencyStore"/>).
/// </summary>
internal static class IdempotencyRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the store: the one in Valkey when the host registered the shared cache (<c>AddFrappeCaching</c>), so every
        /// replica replays the same answer, and one in memory otherwise. A store the host registered before this call is kept.
        /// </summary>
        public IServiceCollection AddFrappeIdempotency()
        {
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IIdempotencyStore>(provider => provider.GetService<ValkeyValues>() is { } values
                ? new ValkeyIdempotencyStore(values)
                : new InMemoryIdempotencyStore(provider.GetRequiredService<TimeProvider>()));
            return services;
        }
    }
}
