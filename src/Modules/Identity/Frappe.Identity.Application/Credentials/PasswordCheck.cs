using Frappe.Identity.Domain.Credentials;

namespace Frappe.Identity.Application.Credentials;

/// <summary>
/// Checks a typed password against what is stored (log-in, step-up, change). Every refusal looks the same from outside: a wrong password, an
/// unknown subject and an unusable candidate all cost one hash (the unknown subject verifies a dummy hash made under the live parameters) and
/// then wait until <see cref="RefusalFloor"/> has passed since the call began, so neither the answer nor its time says which case it was (SEC-06).
/// A match answers at once.
/// </summary>
public sealed class PasswordCheck(IPasswordHasher hasher, TimeProvider time)
{
    /// <summary>The least time a refusal takes.</summary>
    public static readonly TimeSpan RefusalFloor = TimeSpan.FromMilliseconds(500);

    // Made on first use from the live hasher, so its cost follows the configured parameters; the text is random and never leaves this object.
    private readonly Lazy<PasswordHash> _dummy = new(() =>
        hasher.Hash(Password.Create(Guid.NewGuid().ToString("N")).Value));

    /// <summary>
    /// Whether <paramref name="candidate"/> is the password <paramref name="stored"/> was made from; <paramref name="stored"/> is
    /// <see langword="null"/> when the subject does not exist. A <see langword="false"/> answer is never faster than <see cref="RefusalFloor"/>.
    /// </summary>
    public async Task<bool> MatchesAsync(PasswordHash? stored, string? candidate, CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        if (Matches(stored, candidate))
        {
            return true;
        }

        var remaining = RefusalFloor - time.GetElapsedTime(started);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, time, cancellationToken);
        }

        return false;
    }

    private bool Matches(PasswordHash? stored, string? candidate)
    {
        // A candidate that cannot be a password is not hashed (it would cost an attacker nothing to make it huge); the floor still applies.
        if (string.IsNullOrEmpty(candidate) || Password.IsTooLong(candidate))
        {
            return false;
        }

        if (stored is null)
        {
            _ = hasher.Verify(_dummy.Value, candidate);
            return false;
        }

        return hasher.Verify(stored, candidate);
    }
}
