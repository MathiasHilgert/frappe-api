using System.Reflection;
using Frappe.Identity.Application;
using Frappe.Identity.Contracts;
using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Modularity;
using Frappe.Platform.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Identity.Infrastructure;

public sealed class IdentityModule : IModule
{
    public const string Schema = "identity";

    public string Name => "identity";

    public IReadOnlyList<Assembly> MessagingAssemblies { get; } =
        [typeof(IdentityApplicationMarker).Assembly, typeof(IdentityContractsMarker).Assembly];

    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddModuleDbContext<IdentityDbContext>(Schema);
}
