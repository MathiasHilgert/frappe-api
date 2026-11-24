using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Limits;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Credentials;
using Frappe.Identity.UnitTests.Codes;
using Frappe.Results;
using Microsoft.Extensions.Time.Testing;

namespace Frappe.Identity.UnitTests.Credentials;

/// <summary>Asking a signed-in principal for their password again takes a slot per principal first, whatever the outcome (F4, SEC-14).</summary>
public sealed class PasswordRecheckTests
{
    private static readonly PasswordHash Stored = new("stored-hash");
    private static readonly Guid Principal = Guid.Parse("0198f2a0-0000-7000-8000-000000000001");

    private readonly FakeAttemptLimiter _limiter = new();
    private readonly FakeTimeProvider _time = new();
    private readonly CountingHasher _hasher = new();

    private PasswordRecheck Recheck() => new(_limiter, new PasswordCheck(_hasher, _time));

    [Test]
    public async Task A_recheck_takes_a_slot_keyed_by_the_principal_under_the_fail_closed_flow()
    {
        // Act
        var result = await Recheck().MatchesAsync(Principal, Stored, "right password", default);

        // Assert
        var hit = _limiter.Hits.Single();
        using (Assert.Multiple())
        {
            await Assert.That(result.Value).IsTrue();
            await Assert.That(hit.Flow).IsEqualTo(PasswordRecheck.Flow);
            await Assert.That(hit.Subject).IsEqualTo(Principal.ToString("N"));
            await Assert.That(PasswordRecheck.Flow.Name).IsEqualTo("password.recheck");
            await Assert.That(PasswordRecheck.Flow.Limit).IsEqualTo(10);
            await Assert.That(PasswordRecheck.Flow.Window).IsEqualTo(TimeSpan.FromMinutes(15));
            await Assert.That(PasswordRecheck.Flow.OnFailure).IsEqualTo(LimitFailure.FailClosed);
        }
    }

    [Test]
    public async Task A_wrong_password_is_a_plain_false_after_the_floor_and_still_took_its_slot()
    {
        // Act
        var task = Recheck().MatchesAsync(Principal, Stored, "wrong password", default);
        var early = task.IsCompleted;
        _time.Advance(PasswordCheck.RefusalFloor);
        var result = await task;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(early).IsFalse();
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Value).IsFalse();
            await Assert.That(_limiter.Hits.Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Once_the_slots_are_gone_the_answer_is_a_throttle_and_the_password_is_not_even_looked_at()
    {
        // Arrange
        _limiter.Answer = AttemptVerdict.Refused(TimeSpan.FromMinutes(7));

        // Act
        var result = await Recheck().MatchesAsync(Principal, Stored, "right password", default);

        // Assert
        var error = (RateLimitedError)result.Errors.Single();
        using (Assert.Multiple())
        {
            await Assert.That(error.Code).IsEqualTo("http.rate_limited");
            await Assert.That(error.RetryAfterSeconds).IsEqualTo(420);
            await Assert.That(_hasher.Verified).IsEqualTo(0);
        }
    }

    [Test]
    public async Task While_rechecks_cannot_be_counted_none_is_judged_not_even_the_right_password()
    {
        // Arrange
        _limiter.Answer = AttemptVerdict.Unavailable;

        // Act
        var result = await Recheck().MatchesAsync(Principal, Stored, "right password", default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Errors.Single()).IsTypeOf<UnavailableError>();
            await Assert.That(result.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.AttemptsUnavailable);
            await Assert.That(_hasher.Verified).IsEqualTo(0);
        }
    }

    /// <summary>A hasher that knows one correct password and counts the verifications asked of it.</summary>
    private sealed class CountingHasher : IPasswordHasher
    {
        public int Verified { get; private set; }

        public PasswordHash Hash(Password password) => new("dummy-hash");

        public bool Verify(PasswordHash hash, string candidate)
        {
            Verified++;
            return hash == Stored && candidate == "right password";
        }

        public bool NeedsRehash(PasswordHash hash) => false;
    }
}
