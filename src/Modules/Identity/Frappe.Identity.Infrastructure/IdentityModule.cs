using System.Reflection;
using Frappe.Identity.Application;
using Frappe.Identity.Application.Account;
using Frappe.Identity.Application.Owners;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Contracts;
using Frappe.Identity.Infrastructure.Endpoints;
using Frappe.Identity.Infrastructure.Localization;
using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Identity.Infrastructure.Security;
using Frappe.Identity.Infrastructure.Sessions;
using Frappe.Modularity;
using Frappe.Platform.Localization;
using Frappe.Platform.Messaging;
using Frappe.Platform.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quartz;
using Wolverine;

namespace Frappe.Identity.Infrastructure;

public sealed class IdentityModule : IModule
{
    public const string Schema = "identity";

    public string Name => "identity";

    public IReadOnlyList<Assembly> MessagingAssemblies { get; } =
        [typeof(IdentityApplicationMarker).Assembly, typeof(IdentityContractsMarker).Assembly];

    public IReadOnlyList<string> Meters { get; } = [IdentityMetrics.MeterName];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IdentityDbContext>(Schema);
        services.AddScoped<IPrincipalRepository, PrincipalRepository>();
        services.AddScoped<IOwnerReads, OwnerReads>();
        services.AddMessageCatalog(typeof(IdentityModule).Assembly);

        // Both checks run when the host starts, so commands that only compose it (resources setup) need no secrets.
        services.AddSingleton<IValidateOptions<Argon2Options>, Argon2OptionsValidator>();
        services.AddSingleton<IValidateOptions<SecretHashingOptions>, SecretHashingOptionsValidator>();
        services.AddSingleton<IValidateOptions<AttemptLimiterOptions>, AttemptLimiterOptionsValidator>();
        services.AddSingleton<IValidateOptions<BreachedPasswordOptions>, BreachedPasswordOptionsValidator>();
        services.AddOptions<Argon2Options>()
            .BindConfiguration(Argon2Options.SectionName)
            .ValidateOnStart();
        services.AddOptions<SecretHashingOptions>()
            .BindConfiguration(SecretHashingOptions.SectionName)
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<ISecretHasher, HmacSecretHasher>();
        services.AddSingleton<ISecretGenerator, RandomSecretGenerator>();
        services.AddSingleton<DecoyPassword>();

        services.AddSessionAuthentication(configuration);
        AddPasswordPolicy(services);
        services.AddSingleton<IdentityMetrics>();
        services.AddSingleton<IIdentityTelemetry>(sp => sp.GetRequiredService<IdentityMetrics>());
        services.AddOptions<AttemptLimiterOptions>()
            .BindConfiguration(AttemptLimiterOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IAttemptLimiter, ValkeyAttemptLimiter>();
        services.AddScoped<VerificationCodeIssuer>();
        services.AddScoped<PasswordReverifier>();
        services.AddSingleton<IPlatformLocales, PlatformLocales>();
        AddRecurringJobs(services);
    }

    public void ConfigureMessaging(WolverineOptions options)
    {
        // The breach checker is a typed HttpClient, an opaque factory registration Wolverine cannot build inline,
        // and so is the password policy that depends on it.
        options.CodeGeneration.AlwaysUseServiceLocationFor<IBreachedPasswordChecker>();
        options.CodeGeneration.AlwaysUseServiceLocationFor<PasswordPolicy>();
    }

    public void MapEndpoints(RouteGroupBuilder group)
    {
        group.MapOwnerRegistrationEndpoints();
        group.MapOwnerVerificationEndpoints();
        group.MapSessionEndpoints();
        group.MapSessionManagementEndpoints();
        group.MapAccountSecurityEndpoints();
        group.MapEmailChangeEndpoints();
        group.MapPasswordResetEndpoints();
    }

    // Global UTC: the purge has no local meaning. 03:00 keeps it away from the midnight rush of other sweeps.
    private static void AddRecurringJobs(IServiceCollection services) =>
        services.AddRecurringCommand<PurgePendingOwners>(nameof(PurgePendingOwners), "0 0 3 * * ?");

    private static void AddPasswordPolicy(IServiceCollection services)
    {
        services.AddOptions<BreachedPasswordOptions>()
            .BindConfiguration(BreachedPasswordOptions.SectionName)
            .ValidateOnStart();
        services.AddHttpClient<IBreachedPasswordChecker, HibpBreachedPasswordChecker>((sp, http) =>
            {
                var options = sp.GetRequiredService<IOptions<BreachedPasswordOptions>>().Value;
                http.BaseAddress = options.BaseAddress;
                // Backstop only: the checker's own per-call timeout fails open well before this.
                http.Timeout = TimeSpan.FromMilliseconds(options.TimeoutMilliseconds * 2);
            });
        services.AddScoped<PasswordPolicy>();
    }
}
