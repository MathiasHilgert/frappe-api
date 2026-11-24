using Frappe.Http;
using Frappe.Notifications.Http.Sends;
using Frappe.Notifications.Http.Webhooks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Frappe.Notifications.Http;

/// <summary>
/// The HTTP facet of Notifications. The provider webhooks are mapped in every environment; the explicit send route only when the host
/// environment is Development: that is a composition decision made here, not a use-case rule, and the command behind it stays the same
/// when the campaigns API arrives (S1, S2, S10).
/// </summary>
public sealed class NotificationsHttp : IHttpModule
{
    /// <summary>The rate-limit policy of the provider webhooks, with its own budget apart from the anonymous one.</summary>
    public const string WebhookRateLimitPolicy = "Notifications:Webhooks:RateLimit";

    /// <summary>
    /// What one client address may call the webhooks per window by default (<c>Notifications:Webhooks:RateLimit:PermitLimit</c> and
    /// <c>Window</c> change it): Resend's few sending addresses make a burst, so the budget is well above the anonymous one.
    /// </summary>
    private static readonly AnonymousRateLimit WebhookRateLimit = new(1200, TimeSpan.FromMinutes(1));

    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddErrorMessages(typeof(NotificationsHttp).Assembly);
        services.AddFrappeRateLimitPolicy(WebhookRateLimitPolicy, WebhookRateLimitPolicy, WebhookRateLimit);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder v1)
    {
        ArgumentNullException.ThrowIfNull(v1);
        v1.MapResendWebhookEndpoints();
        if (v1.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            v1.MapSendNotificationEndpoints();
        }
    }
}
