using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Platform.Persistence;

/// <summary>
/// Registers a <see cref="FrappeDbContext"/> in place of the plain <c>AddDbContext</c>. The messaging adapter
/// registers one so every context joins its outbox transaction, without Persistence referencing the bus.
/// </summary>
public interface IDbContextRegistrar
{
    /// <summary>Registers <typeparamref name="TContext"/> as a scoped context with singleton options.</summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="services">The services.</param>
    /// <param name="configure">Configures the context's options.</param>
    void Register<TContext>(
        IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> configure
    )
        where TContext : FrappeDbContext;
}
