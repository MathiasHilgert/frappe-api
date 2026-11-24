using Microsoft.EntityFrameworkCore;

namespace Frappe.Messaging.Abstractions;

/// <summary>A module's outbox relay: <paramref name="Module"/> publishes the outbox of <typeparamref name="TContext"/>.</summary>
/// <param name="Module">The producer module, which owns the stream.</param>
/// <param name="Section">The configuration section of the relay options, or <see langword="null"/> for the transport's default.</param>
internal sealed record OutboxRelayDeclaration<TContext>(string Module, string? Section) : IMessagingDeclaration
    where TContext : DbContext
{
    public void Accept(IMessagingDeclarationVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.Relay<TContext>(this);
    }
}
