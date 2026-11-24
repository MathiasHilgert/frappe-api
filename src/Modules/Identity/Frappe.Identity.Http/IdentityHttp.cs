using Frappe.Http;
using Frappe.Identity.Http.Owners;
using Frappe.Identity.Http.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Frappe.Identity.Http;

/// <summary>
/// The HTTP facet of Identity: the session scheme and its policy, which only routes that ask for a session use (SEC-01), the error texts, and
/// the routes under <c>/v1/identity</c>.
/// </summary>
public sealed class IdentityHttp : IHttpModule
{
    /// <summary>The OpenAPI tag of the module's operations.</summary>
    internal const string Tag = "Identity";

    /// <summary>The rate-limit policy of registrations, with its own budget apart from the anonymous one.</summary>
    public const string RegistrationRateLimitPolicy = "Identity:Registration:RateLimit";

    /// <summary>The rate-limit policy of log-ins per client address, with its own budget apart from the anonymous one.</summary>
    public const string LogInRateLimitPolicy = "Identity:LogIn:RateLimit";

    private const string Prefix = "/identity";

    /// <summary>
    /// What one client address may register per window by default (<c>Identity:Registration:RateLimit:PermitLimit</c> and <c>Window</c>
    /// change it): each registration hashes a password and may send a mail, so the budget is well below the anonymous one.
    /// </summary>
    private static readonly AnonymousRateLimit RegistrationRateLimit = new(10, TimeSpan.FromMinutes(1));

    /// <summary>
    /// What one client address may attempt to log in per window by default (<c>Identity:LogIn:RateLimit:PermitLimit</c> and <c>Window</c> change
    /// it): each attempt hashes a password, so the budget is well below the anonymous one. The address typed is limited apart, in the limiter.
    /// </summary>
    private static readonly AnonymousRateLimit LogInRateLimit = new(20, TimeSpan.FromMinutes(1));

    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddErrorMessages(typeof(IdentityHttp).Assembly);
        services.TryAddSingleton(TimeProvider.System);
        services.AddFrappeRateLimitPolicy(RegistrationRateLimitPolicy, RegistrationRateLimitPolicy, RegistrationRateLimit);
        services.AddFrappeRateLimitPolicy(LogInRateLimitPolicy, LogInRateLimitPolicy, LogInRateLimit);
        services.AddSessionAuthentication();
        services.AddOwnerPolicy();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder v1)
    {
        ArgumentNullException.ThrowIfNull(v1);
        var identity = v1.MapGroup(Prefix);
        identity.MapSessionEndpoints();
        identity.MapOwnerEndpoints();
        identity.MapLogInEndpoints();
        identity.MapMeEndpoints();
    }
}
