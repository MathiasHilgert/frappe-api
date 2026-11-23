using Frappe.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Persistence;

/// <summary>Registers the rules a module uses to turn a database constraint failure into a business rejection.</summary>
public static class CommitRejectionRegistration
{
    extension<TBuilder>(TBuilder builder)
        where TBuilder : DbContextOptionsBuilder
    {
        /// <summary>
        /// Has a save of the context made outside its unit of work (a background job's, the outbox relay's) fail with the same
        /// rejection the commit of a command would, by <paramref name="rejections"/>. <c>AddModuleDbContext</c> does it for
        /// the rules a module registered; a context built by hand (a test's) asks for it here.
        /// </summary>
        public TBuilder UseCommitRejections(CommitRejectionMap rejections)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(rejections);
            builder.AddInterceptors(new CommitRejectionInterceptor(rejections));
            return builder;
        }
    }

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the rules that turn a refused commit of <typeparamref name="TContext"/> into the module's
        /// <see cref="ICommitRejection"/> (UOW-05); its unit of work (<see cref="UnitOfWorkRegistration.AddUnitOfWork"/>)
        /// reads them when it saves. Registering a module's rules twice is a mistake and throws.
        /// </summary>
        public IServiceCollection AddCommitRejections<TContext>(Action<CommitRejectionMap> configure)
            where TContext : DbContext
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);
            if (services.Any(descriptor => descriptor.ServiceType == typeof(CommitRejectionMap) && Equals(descriptor.ServiceKey, typeof(TContext))))
            {
                throw new InvalidOperationException($"The commit rejections of {typeof(TContext).Name} are already registered.");
            }

            var map = new CommitRejectionMap();
            configure(map);
            return services.AddKeyedSingleton<CommitRejectionMap>(typeof(TContext), map);
        }
    }
}
