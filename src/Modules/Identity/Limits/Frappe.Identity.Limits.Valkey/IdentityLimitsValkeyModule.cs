using Frappe.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Identity.Limits.Valkey;

/// <summary>
/// The Valkey-backed attempt limiter as the Api lists it: Identity's attempt limits count in Valkey, which only the Api reaches. The limiter
/// itself is registered here when its port arrives; today the plug-in only holds its place in the composition.
/// </summary>
public sealed class IdentityLimitsValkeyModule : IModule
{
    /// <inheritdoc />
    public void AddServices(IServiceCollection services) => ArgumentNullException.ThrowIfNull(services);
}
