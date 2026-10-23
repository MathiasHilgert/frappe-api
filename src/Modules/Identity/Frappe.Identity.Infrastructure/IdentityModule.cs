using System.Reflection;
using Frappe.Identity.Application;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Contracts;
using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Identity.Infrastructure.Security;
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

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IdentityDbContext>(Schema);
        services.AddScoped<IPrincipalRepository, PrincipalRepository>();

        // Both checks run when the host starts, so commands that only compose it (resources setup) need no secrets.
        services.AddOptions<Argon2Options>()
            .Bind(configuration.GetSection(Argon2Options.Section))
            .Validate(options => options.IsValid(),
                $"'{Argon2Options.Section}' is below the Argon2id minimum (m={Argon2Options.MinimumMemorySizeKib}, t={Argon2Options.MinimumIterations}, p=1..16).")
            .ValidateOnStart();
        services.AddOptions<SecretHashingOptions>()
            .Bind(configuration.GetSection(SecretHashingOptions.Section))
            .Validate(options => options.IsValid(),
                $"'{SecretHashingOptions.Section}:Key' is missing or shorter than {SecretHashingOptions.MinimumKeyLength} characters.")
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<ISecretHasher, HmacSecretHasher>();
        services.AddSingleton<ISecretGenerator, RandomSecretGenerator>();
    }
}
