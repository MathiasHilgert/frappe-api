using Frappe.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Frappe.Persistence;

public static class UnitOfWorkRegistration
{
    /// <summary>
    /// Registers the unit of work of <paramref name="module"/> (the lower-case module name of its namespaces,
    /// <c>Frappe.&lt;Module&gt;.*</c>) over the scope's <typeparamref name="TContext"/>, so the platform runs each of the
    /// module's commands in it and commits the command's changes once. The unit hands the domain events it saves to the
    /// scope's <c>IDomainEventHandler&lt;TEvent&gt;</c>s before the save and to its
    /// <c>IAfterCommitDomainEventHandler&lt;TEvent&gt;</c>s after the commit.
    /// </summary>
    public static IServiceCollection AddUnitOfWork<TContext>(this IServiceCollection services, string module)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        services.TryAddKeyedScoped<IUnitOfWork>(module, (provider, _) => new EfUnitOfWork<TContext>(
            provider.GetRequiredService<TContext>(),
            provider,
            (ILogger?)provider.GetService<ILoggerFactory>()?.CreateLogger<EfUnitOfWork<TContext>>() ?? NullLogger.Instance));
        return services;
    }
}
