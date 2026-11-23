using Frappe.IntegrationEvents;

namespace Frappe.Messaging.Inbox;

/// <summary>
/// A step a consumer runs before it claims the message, with no database transaction open: the place for a slow call to
/// another system (an external provider) that must not keep a connection idle in a transaction for its whole duration.
/// <list type="bullet">
/// <item>A message the consumer already handled never reaches it: the consumer reads its inbox first.</item>
/// <item>It may run more than once for one message: a crash, or a second delivery racing the first, between it and the
/// claim's commit runs it again. It must therefore be idempotent towards the other system (an idempotency key derived from
/// <see cref="CurrentMessage.Id"/>).</item>
/// <item>What it stages in the module's context is saved in the claim's transaction, with the inbox record, or not at all.</item>
/// <item>Throwing means "retry later": nothing is claimed and the message comes back.</item>
/// </list>
/// </summary>
#pragma warning disable CA1711 // "Handler" names the port's role; this is not a .NET event delegate.
public interface IBeforeClaimHandler<in TEvent>
#pragma warning restore CA1711
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
