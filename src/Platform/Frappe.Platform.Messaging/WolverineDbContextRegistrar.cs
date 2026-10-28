using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore;

namespace Frappe.Platform.Messaging;

// Every module context maps the envelope tables, so the outbox writes them in the handler's transaction.
internal sealed class WolverineDbContextRegistrar : IDbContextRegistrar
{
    public void Register<TContext>(
        IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> configure
    )
        where TContext : FrappeDbContext =>
        services.AddDbContextWithWolverineIntegration<TContext>(
            configure,
            MessagingExtensions.Schema
        );
}
