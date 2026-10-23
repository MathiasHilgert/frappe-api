using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Domain;
using Wolverine;

namespace Frappe.Identity.Application.Principals.Purge;

public static class PurgePendingOwnersHandler
{
    /// <summary>Bounds one run's transaction; whatever is left is picked up by the next run.</summary>
    public const int BatchSize = 500;

    // The query only narrows the candidates; the aggregate decides, so the purge rule lives in one place.
    // A deleted aggregate leaves the change tracker before its events can be scraped, so they are returned
    // as cascading messages instead: Wolverine writes them to the outbox in this handler's transaction.
    public static async Task<OutgoingMessages> Handle(
        PurgePendingOwners command,
        IPrincipalRepository repository,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow() - Principal.PendingGracePeriod;
        var candidates = await repository.ListPendingRegisteredBeforeAsync(cutoff, BatchSize, cancellationToken);

        var events = new OutgoingMessages();
        foreach (var principal in candidates)
        {
            if (principal.PurgeAbandonedRegistration(clock).IsSuccess)
            {
                events.AddRange(((IHasDomainEvents)principal).DequeueDomainEvents());
                repository.Remove(principal);
            }
        }

        await repository.SaveChangesAsync(cancellationToken);
        return events;
    }
}
