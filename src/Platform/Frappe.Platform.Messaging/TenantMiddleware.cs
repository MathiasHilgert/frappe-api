using System.Globalization;
using System.Reflection;
using Frappe.Kernel.Messaging;
using Wolverine;

namespace Frappe.Platform.Messaging;

/// <summary>
/// Opens the tenant scope from the envelope's tenant id before the handler and its database transaction;
/// a message without a tenant fails here unless it is declared global. Public because generated handler
/// code calls it; <see cref="TenantPolicy"/> puts the call first in every chain.
/// </summary>
public static class TenantMiddleware
{
    /// <summary>Opens the scope; called by generated handler code first, before any database work.</summary>
    /// <param name="envelope">The envelope being handled.</param>
    /// <returns>
    /// The scope, which the generated code disposes after the handler, or <see langword="null"/> for a global message.
    /// </returns>
    /// <exception cref="InvalidOperationException">The message carries no valid tenant and is not global.</exception>
    public static IDisposable? Before(Envelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (Guid.TryParse(envelope.TenantId, CultureInfo.InvariantCulture, out Guid businessId))
        {
            return TenantScope.Begin(businessId, branchId: null, TenantScope.SystemPrincipalId);
        }

        if (envelope.Message?.GetType().GetCustomAttribute<GlobalMessageAttribute>() is not null)
        {
            return null;
        }

        throw new InvalidOperationException(
            $"Message {envelope.MessageType} has no tenant and is not declared global. Send it inside "
                + "TenantScope.Begin (a handler's own sends inherit its tenant), or mark it [GlobalMessage] "
                + "when it runs for no tenant."
        );
    }
}
