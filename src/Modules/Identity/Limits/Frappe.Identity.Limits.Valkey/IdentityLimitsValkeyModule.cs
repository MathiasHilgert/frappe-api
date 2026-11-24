using Frappe.Identity.Application.Limits;
using Frappe.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Frappe.Identity.Limits.Valkey;

/// <summary>
/// The Valkey-backed attempt limiter as the Api lists it: Identity's attempt limits count in Valkey, which only the Api reaches. It uses the
/// window counter the host's <c>AddFrappeCaching</c> registers, and settings from <c>Identity:Limits:&lt;flow&gt;</c>
/// (<see cref="AttemptLimitOptions"/>).
/// </summary>
public sealed class IdentityLimitsValkeyModule : IModule
{
    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<AttemptLimitOptions>()
            .Configure<IConfiguration>((options, configuration) => configuration.GetSection(AttemptLimitOptions.Section).Bind(options.Flows))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AttemptLimitOptions>, AttemptLimitOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAttemptLimiter, ValkeyAttemptLimiter>();
    }
}
