using Frappe.Aggregates;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Observability;

/// <summary>Counts business successes once the unit of work that raised them committed (OBS-06, OBS-08).</summary>
public static class CountOnCommitRegistration
{
    /// <param name="services">The module's services.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Counts <typeparamref name="TEvent"/> after the commit that carried it, never after a rollback: registers an
        /// <see cref="IAfterCommitDomainEventHandler{TEvent}"/> that resolves <typeparamref name="TInstrument"/> from the
        /// container and calls <paramref name="count"/> with it. A module states what it counts in one line instead of writing
        /// a counting handler. Several calls for one event each count it, so a conditional counter (a replacement) is its own call
        /// that tests the event. The handler is best effort like every after-commit step: a failure is logged and the command
        /// still succeeds (OBS-10).
        /// </summary>
        /// <typeparam name="TEvent">The domain event whose commit is the business success.</typeparam>
        /// <typeparam name="TInstrument">
        /// The module's metrics class (<c>IdentityMetrics</c>), a service of the container. The counters generated for a metric
        /// share no interface, so the class that holds them is what <paramref name="count"/> receives.
        /// </typeparam>
        /// <param name="count">Records the event on the instrument, with the tags the event carries.</param>
        public IServiceCollection CountOnCommit<TEvent, TInstrument>(Action<TInstrument, TEvent> count)
            where TEvent : IDomainEvent
            where TInstrument : class
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(count);
            services.AddScoped<IAfterCommitDomainEventHandler<TEvent>>(provider =>
                new CountingAfterCommit<TEvent, TInstrument>(provider.GetRequiredService<TInstrument>(), count));
            return services;
        }
    }

    /// <summary>Counts a domain event on an instrument only after the transaction committed, so a rolled-back change never counts.</summary>
    private sealed class CountingAfterCommit<TEvent, TInstrument>(TInstrument instrument, Action<TInstrument, TEvent> count)
        : IAfterCommitDomainEventHandler<TEvent>
        where TEvent : IDomainEvent
    {
        public Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(domainEvent);
            count(instrument, domainEvent);
            return Task.CompletedTask;
        }
    }
}
