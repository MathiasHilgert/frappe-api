using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Preferences;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Notifications.Application.Requests.Unsubscribe;

/// <summary>
/// Handles <see cref="Unsubscribe"/>: verifies the token, and stages the opt-out unless the recipient already has it (so a repeat stages
/// nothing and counts nothing). Two calls at once leave one row: the commit's rule repairs the second (see the module's rejections).
/// </summary>
internal sealed class UnsubscribeHandler(IUnsubscribeTokens tokens, IPreferenceStore preferences, IClock clock) : ICommandHandler<Unsubscribe, Unit>
{
    public async Task<Result<Unit>> HandleAsync(Unsubscribe command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var claim = tokens.Verify(command.Token);
        if (!claim.IsSuccess)
        {
            return Result.Failure<Unit>(claim.Errors);
        }

        var (recipient, category) = claim.Value;
        if (await preferences.IsOptedOutAsync([recipient], category, cancellationToken))
        {
            return Unit.Value;
        }

        var preference = Preference.OptOut(recipient, category, clock.GetCurrentInstant());
        if (!preference.IsSuccess)
        {
            return Result.Failure<Unit>(preference.Errors);
        }

        preferences.Stage(preference.Value);
        return Unit.Value;
    }
}
