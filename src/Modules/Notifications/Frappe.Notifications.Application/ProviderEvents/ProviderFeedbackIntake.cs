using Frappe.Results;
using Frappe.UseCases;
using RecordFeedback = Frappe.Notifications.Application.ProviderEvents.RecordProviderFeedback.RecordProviderFeedback;

namespace Frappe.Notifications.Application.ProviderEvents;

/// <summary>
/// Where a webhook hands over what the provider said: it holds the message's lock (<see cref="IProviderMessageLock"/>) around the
/// <see cref="RecordFeedback"/> command, so the lock outlives the command's commit and the Worker's claim cannot slip between the
/// delivery lookup and the parking (Flow 3).
/// </summary>
public sealed class ProviderFeedbackIntake(IProviderMessageLock locks, ICommandHandler<RecordFeedback, Unit> handler)
{
    /// <summary>Records the feedback once nobody else is writing about the same message.</summary>
    public async Task<Result<Unit>> RecordAsync(ProviderFeedback feedback, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        await using var held = feedback.ProviderMessageId is { } messageId
            ? await locks.AcquireAsync(feedback.Provider, messageId, cancellationToken)
            : null;
        return await handler.HandleAsync(new RecordFeedback(feedback), cancellationToken);
    }
}
