using Frappe.Identity.Application.Codes;
using Frappe.Identity.Application.Limits;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Codes;
using Frappe.Results;
using Microsoft.Extensions.Time.Testing;
using NodaTime;

namespace Frappe.Identity.UnitTests.Codes;

/// <summary>Issuing replaces the live code in one step, hands the plaintext out once and spends a send budget first (S5).</summary>
public sealed class CodeIssuerTests
{
    private readonly FakeOneTimeCodes _codes = new();
    private readonly FakeAttemptLimiter _limiter = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero));

    private CodeIssuer Issuer() => new(_codes, CodeFixtures.Hasher(), _limiter, _time);

    [Test]
    public async Task A_code_is_six_digits_stored_only_as_its_hash_and_valid_for_fifteen_minutes()
    {
        // Act
        var result = await Issuer().IssueAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, "ana@example.com", default);

        // Assert
        var stored = _codes.Stored.Single();
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Value.Plaintext).Matches("^[0-9]{6}$");
            await Assert.That(result.Value.Id).IsEqualTo(stored.Id);
            await Assert.That(result.Value.ExpiresAt).IsEqualTo(stored.ExpiresAt);
            await Assert.That(stored.ExpiresAt - stored.IssuedAt).IsEqualTo(Duration.FromMinutes(15));
            await Assert.That(stored.Target).IsEqualTo("ana@example.com");
            await Assert.That(stored.Purpose).IsEqualTo(CodePurpose.EmailVerification);
            await Assert.That(stored.CodeHmac).IsEqualTo(CodeFixtures.Hasher().Hash(result.Value.Plaintext));
            await Assert.That(CodeFixtures.Hasher().Matches(stored.CodeHmac, result.Value.Plaintext)).IsTrue();
        }
    }

    [Test]
    public async Task The_plaintext_is_never_printed()
    {
        // Act
        var result = await Issuer().IssueAsync(CodeFixtures.Principal, CodePurpose.PasswordReset, "ana@example.com", default);

        // Assert
        await Assert.That(result.Value.ToString()).DoesNotContain(result.Value.Plaintext);
    }

    [Test]
    public async Task Issuing_again_invalidates_the_live_code_of_that_purpose_and_stages_the_new_one()
    {
        // Arrange
        var issuer = Issuer();
        var first = (await issuer.IssueAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, "ana@example.com", default)).Value;
        _time.Advance(TimeSpan.FromMinutes(1));

        // Act
        var second = (await issuer.IssueAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, "ana@example.com", default)).Value;

        // Assert
        var old = _codes.Stored.Single(c => c.Id == first.Id);
        using (Assert.Multiple())
        {
            await Assert.That(_codes.Stored.Count).IsEqualTo(2);
            await Assert.That(old.InvalidatedAt).IsEqualTo(Instant.FromUtc(2026, 11, 24, 12, 1));
            await Assert.That(_codes.Stored.Count(c => c.HoldsLiveSlot)).IsEqualTo(1);
            await Assert.That(_codes.Stored.Single(c => c.HoldsLiveSlot).Id).IsEqualTo(second.Id);
        }
    }

    [Test]
    public async Task Another_purpose_or_another_principal_keeps_its_live_code()
    {
        // Arrange
        var issuer = Issuer();
        var other = Guid.Parse("0198f2a0-0000-7000-8000-000000000002");
        await issuer.IssueAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, "ana@example.com", default);

        // Act
        await issuer.IssueAsync(CodeFixtures.Principal, CodePurpose.PasswordReset, "ana@example.com", default);
        await issuer.IssueAsync(other, CodePurpose.EmailVerification, "bo@example.com", default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(_codes.Stored.Count).IsEqualTo(3);
            await Assert.That(_codes.Stored.All(c => c.HoldsLiveSlot)).IsTrue();
        }
    }

    [Test]
    public async Task A_code_that_expired_unredeemed_is_replaced_too_because_it_still_holds_the_slot()
    {
        // Arrange
        var issuer = Issuer();
        var first = (await issuer.IssueAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, "ana@example.com", default)).Value;
        _time.Advance(TimeSpan.FromHours(3));

        // Act
        await issuer.IssueAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, "ana@example.com", default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(_codes.Stored.Single(c => c.Id == first.Id).InvalidatedAt).IsNotNull();
            await Assert.That(_codes.Stored.Count(c => c.HoldsLiveSlot)).IsEqualTo(1);
        }
    }

    [Test]
    public async Task The_send_budget_is_counted_per_purpose_and_principal_before_anything_is_staged()
    {
        // Act
        await Issuer().IssueAsync(CodeFixtures.Principal, CodePurpose.EmailChange, "ana@example.com", default);

        // Assert
        var hit = _limiter.Hits.Single();
        using (Assert.Multiple())
        {
            await Assert.That(hit.Flow).IsEqualTo(CodeLimits.Send);
            await Assert.That(hit.Subject).IsEqualTo($"EmailChange:{CodeFixtures.Principal:N}");
            await Assert.That(CodeLimits.Send.Limit).IsEqualTo(5);
            await Assert.That(CodeLimits.Send.Window).IsEqualTo(TimeSpan.FromHours(1));
            await Assert.That(CodeLimits.Send.OnFailure).IsEqualTo(LimitFailure.FailOpen);
        }
    }

    [Test]
    public async Task A_spent_budget_issues_nothing_and_leaves_the_live_code_working()
    {
        // Arrange
        var issuer = Issuer();
        var first = (await issuer.IssueAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, "ana@example.com", default)).Value;
        _limiter.Answer = AttemptVerdict.Refused(TimeSpan.FromMinutes(20));

        // Act
        var result = await issuer.IssueAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, "ana@example.com", default);

        // Assert
        var error = (RateLimitedError)result.Errors.Single();
        using (Assert.Multiple())
        {
            await Assert.That(error.Code).IsEqualTo(IdentityErrorCodes.CodeSendLimited);
            await Assert.That(error.RetryAfterSeconds).IsEqualTo(1200);
            await Assert.That(_codes.Stored.Count).IsEqualTo(1);
            await Assert.That(_codes.Stored.Single(c => c.Id == first.Id).HoldsLiveSlot).IsTrue();
        }
    }

    [Test]
    public async Task A_counter_that_is_down_does_not_stop_a_code_from_being_sent()
    {
        // Arrange
        _limiter.Answer = AttemptVerdict.Allowed;

        // Act
        var result = await Issuer().IssueAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, "ana@example.com", default);

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
    }
}
