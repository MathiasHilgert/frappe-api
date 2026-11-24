using Frappe.Access.Application;
using Frappe.Access.Contracts;
using Frappe.Modules;
using Frappe.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Frappe.Access.Infrastructure;

/// <summary>
/// The services of the Access module in every host: the use-case gate every handler passes through, the authorizer over the rules the modules
/// declare (<see cref="IRules"/>), and the decisions counter. The caller is not here: each host registers who its callers are (the Api from the
/// session, the Worker as the system), and a host that registers none fails when it resolves a handler.
/// </summary>
public sealed class AccessModule : IModule
{
    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AccessMetrics>();
        services.AddSingleton(provider =>
        {
            var book = new RuleBook();
            foreach (var rules in provider.GetServices<IRules>())
            {
                rules.Define(book);
            }

            return book;
        });

        // The requests are the ones the final service collection has a handler for, so Access needs no reference to the modules that own them
        // and no order in the list of modules.
        services.AddSingleton(_ => UseCaseTable.From(RequestsHandledIn(services)));
        services.AddScoped<AccessScope>();
        services.AddScoped<IUseCaseGate, AccessGate>();
        services.AddScoped<IAuthorizer, Authorizer>();
    }

    private static IEnumerable<Type> RequestsHandledIn(IServiceCollection services) => services
        .Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType is { IsGenericType: true, IsGenericTypeDefinition: false })
        .Where(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
            || descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
        .Select(descriptor => descriptor.ServiceType.GetGenericArguments()[0]);
}
