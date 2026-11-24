using Microsoft.EntityFrameworkCore;

namespace Frappe.Messaging.Abstractions;

/// <summary>A module's message purge: <paramref name="Module"/>'s outbox and inbox in <typeparamref name="TContext"/>.</summary>
/// <param name="Module">The module whose messages are purged.</param>
/// <param name="Section">The configuration section of the retention options, or <see langword="null"/> for the default.</param>
internal sealed record MessagePurgeDeclaration<TContext>(string Module, string? Section) : IMessagingDeclaration
    where TContext : DbContext
{
    public void Accept(IMessagingDeclarationVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.Purge<TContext>(this);
    }
}
