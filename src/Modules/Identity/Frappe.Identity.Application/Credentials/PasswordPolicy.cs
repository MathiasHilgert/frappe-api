using Frappe.Identity.Application.Telemetry;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Credentials;
using Frappe.Results;

namespace Frappe.Identity.Application.Credentials;

/// <summary>
/// What a new password must be (S4): 10 to 128 characters and not in a public breach list. A list that cannot answer lets the password
/// through ("si cae, dejar pasar"), counted as <see cref="BreachCheckOutcome.Unavailable"/>.
/// </summary>
public sealed class PasswordPolicy(IBreachedPasswords breached, CredentialsMetrics metrics, TimeProvider time)
{
    /// <summary>The password <paramref name="raw"/> spells when it meets the policy, or why it does not.</summary>
    public async Task<Result<Password>> AcceptAsync(string? raw, CancellationToken cancellationToken)
    {
        var created = Password.Create(raw);
        if (!created.IsSuccess)
        {
            return created;
        }

        var started = time.GetTimestamp();
        var answer = await breached.IsBreachedAsync(created.Value, cancellationToken);
        var outcome = answer switch
        {
            true => BreachCheckOutcome.Breached,
            false => BreachCheckOutcome.Clean,
            null => BreachCheckOutcome.Unavailable,
        };
        metrics.BreachChecked(time.GetElapsedTime(started), outcome);
        return outcome == BreachCheckOutcome.Breached ? Error.RuleViolation(IdentityErrorCodes.PasswordBreached) : created;
    }
}
