using System.Reflection;
using Frappe.Identity.Application;
using Frappe.Identity.Application.Account;
using Frappe.Identity.Application.Owners;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Principals.Purge;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Contracts;
using Frappe.Identity.Infrastructure.Endpoints;
using Frappe.Identity.Infrastructure.Owners;
using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Identity.Infrastructure.Security;
using Frappe.Identity.Infrastructure.Sessions;
using Frappe.Modularity;
using Frappe.Platform.Localization;
using Frappe.Platform.Messaging.Jobs;
using Frappe.Platform.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wolverine;
using Quartz;

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
        services.AddMessageCatalog(typeof(IdentityModule).Assembly);

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

        services.AddSessionAuthentication(configuration);
        AddPasswordPolicy(services, configuration);
        services.AddSingleton<IdentityMetrics>();
        services.AddOptions<AttemptLimiterOptions>()
            .Bind(configuration.GetSection(AttemptLimiterOptions.Section))
            .Validate(options => options.IsValid(),
                $"'{AttemptLimiterOptions.Section}:TimeoutMilliseconds' must be {AttemptLimiterOptions.MinimumTimeoutMilliseconds}..{AttemptLimiterOptions.MaximumTimeoutMilliseconds}.")
            .ValidateOnStart();
        services.AddSingleton<IAttemptLimiter, ValkeyAttemptLimiter>();
        services.AddScoped<VerificationCodeIssuer>();
        services.AddScoped<VerifyOwnerEmail>();
        services.AddScoped<OwnerSessions>();
        services.AddScoped<PasswordReverifier>();
        services.AddScoped<StepUpRequirement>();
        services.AddScoped<CompleteStepUp>();
        services.AddScoped<ChangePassword>();
        services.AddScoped<ConfirmEmailChange>();
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
        group.MapOwnerRegistration();
        group.MapOwnerVerification();
        group.MapSessionEndpoints();
        group.MapSessionManagement();
        group.MapAccountSecurityEndpoints();
        group.MapEmailChangeEndpoints();
        group.MapPasswordResets();
    }

    // Global UTC: the purge has no local meaning. 03:00 keeps it away from the midnight rush of other sweeps.
    private static void AddRecurringJobs(IServiceCollection services) =>
        services.AddQuartz(quartz =>
            quartz.ScheduleJob<PublishCommandJob<PurgePendingOwners>>(trigger => trigger
                .WithIdentity(nameof(PurgePendingOwners))
                .WithCronSchedule("0 0 3 * * ?", cron => cron.InTimeZone(TimeZoneInfo.Utc))));

    private static void AddPasswordPolicy(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BreachedPasswordOptions>()
            .Bind(configuration.GetSection(BreachedPasswordOptions.Section))
            .Validate(options => options.IsValid(),
                $"'{BreachedPasswordOptions.Section}' needs an absolute BaseAddress and a TimeoutMilliseconds of {BreachedPasswordOptions.MinimumTimeoutMilliseconds}..{BreachedPasswordOptions.MaximumTimeoutMilliseconds}.")
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
