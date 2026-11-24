using Frappe.Identity.Application.Codes;
using Frappe.Identity.Application.Limits;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Codes;
using Frappe.Results;
using Microsoft.Extensions.Time.Testing;
using NodaTime;

namespace Frappe.Identity.UnitTests.Codes;

/// <summary>A guess takes a slot in the limiter first, then is compared in fixed time; every refusal reads the same (S5).</summary>
public sealed class CodeRedeemerTests
{
    private readonly FakeOneTimeCodes _codes = new();
    private readonly FakeAttemptLimiter _limiter = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero));

    private CodeRedeemer Redeemer() => new(_codes, CodeFixtures.Hasher(), _limiter, _time);

    private async Task<IssuedCode> IssueAsync(CodePurpose purpose = CodePurpose.EmailVerification) =>
        (await new CodeIssuer(_codes, CodeFixtures.Hasher(), new FakeAttemptLimiter(), _time)
            .IssueAsync(CodeFixtures.Principal, purpose, "ana@example.com", default)).Value;

    private static string CodeOf(Result<RedeemedCode> result) => result.Errors.Single().Code;

    [Test]
    public async Task The_right_code_redeems_once_and_says_what_it_proved()
    {
        // Arrange
        var issued = await IssueAsync();
        _time.Advance(TimeSpan.FromMinutes(2));

        // Act
        var first = await Redeemer().RedeemAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, issued.Plaintext, default);
        var second = await Redeemer().RedeemAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, issued.Plaintext, default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.Value).IsEqualTo(new RedeemedCode(issued.Id, CodePurpose.EmailVerification, "ana@example.com"));
            await Assert.That(_codes.Stored.Single().RedeemedAt).IsEqualTo(Instant.FromUtc(2026, 11, 24, 12, 2));
            await Assert.That(CodeOf(second)).IsEqualTo(IdentityErrorCodes.CodeInvalid);
        }
    }

    [Test]
    public async Task A_guess_takes_a_slot_keyed_by_the_code_id_under_the_fail_closed_guess_flow()
    {
        // Arrange
        var issued = await IssueAsync();

        // Act
        await Redeemer().RedeemAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, "000000", default);

        // Assert
        var hit = _limiter.Hits.Single();
        using (Assert.Multiple())
        {
            await Assert.That(hit.Flow).IsEqualTo(CodeLimits.Guess);
            await Assert.That(hit.Subject).IsEqualTo(issued.Id.ToString("N"));
            await Assert.That(CodeLimits.Guess.Limit).IsEqualTo(5);
            await Assert.That(CodeLimits.Guess.Window).IsEqualTo(TimeSpan.FromMinutes(15));
            await Assert.That(CodeLimits.Guess.OnFailure).IsEqualTo(LimitFailure.FailClosed);
        }
    }

    [Test]
    [Arguments("000000")]
    [Arguments("12345")]
    [Arguments("1234567")]
    [Arguments("abcdef")]
    [Arguments("")]
    [Arguments(null)]
    public async Task A_wrong_or_malformed_code_is_invalid_and_leaves_the_code_unspent(string? candidate)
    {
        // Arrange
        var issued = await IssueAsync();
        var guess = candidate == issued.Plaintext ? "999999" : candidate;

        // Act
        var result = await Redeemer().RedeemAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, guess, default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(CodeOf(result)).IsEqualTo(IdentityErrorCodes.CodeInvalid);
            await Assert.That(_codes.Stored.Single().RedeemedAt).IsNull();
        }
    }

    [Test]
    public async Task Once_the_slots_are_gone_even_the_right_code_is_invalid()
    {
        // Arrange
        var issued = await IssueAsync();
        _limiter.Answer = AttemptVerdict.Refused(TimeSpan.FromMinutes(10));

        // Act
        var result = await Redeemer().RedeemAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, issued.Plaintext, default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(CodeOf(result)).IsEqualTo(IdentityErrorCodes.CodeInvalid);
            await Assert.That(_codes.Stored.Single().RedeemedAt).IsNull();
        }
    }

    [Test]
    public async Task While_guesses_cannot_be_counted_none_is_judged_not_even_the_right_one()
    {
        // Arrange
        var issued = await IssueAsync();
        _limiter.Answer = AttemptVerdict.Unavailable;

        // Act
        var result = await Redeemer().RedeemAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, issued.Plaintext, default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Errors.Single()).IsTypeOf<UnavailableError>();
            await Assert.That(CodeOf(result)).IsEqualTo(IdentityErrorCodes.AttemptsUnavailable);
            await Assert.That(_codes.Stored.Single().RedeemedAt).IsNull();
        }
    }

    [Test]
    public async Task An_expired_code_is_invalid_though_it_is_right()
    {
        // Arrange
        var issued = await IssueAsync();
        _time.Advance(TimeSpan.FromMinutes(15));

        // Act
        var result = await Redeemer().RedeemAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, issued.Plaintext, default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(CodeOf(result)).IsEqualTo(IdentityErrorCodes.CodeInvalid);
            await Assert.That(_codes.Stored.Single().RedeemedAt).IsNull();
        }
    }

    [Test]
    public async Task A_superseded_code_is_invalid_and_the_new_one_redeems()
    {
        // Arrange
        var old = await IssueAsync();
        var current = await IssueAsync();

        // Act
        var oldResult = await Redeemer().RedeemAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, old.Plaintext, default);
        var currentResult = await Redeemer().RedeemAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, current.Plaintext, default);

        // Assert
        using (Assert.Multiple())
        {
            // The old text is wrong for the live code, unless the two random codes collided (one in a million); the live one is what counts.
            await Assert.That(currentResult.IsSuccess).IsTrue();
            await Assert.That(_codes.Stored.Single(c => c.Id == old.Id).RedeemedAt).IsNull();
            await Assert.That(oldResult.IsSuccess == (old.Plaintext == current.Plaintext)).IsTrue();
        }
    }

    [Test]
    public async Task A_code_of_another_purpose_or_no_code_at_all_is_invalid_without_counting_a_guess()
    {
        // Arrange
        var issued = await IssueAsync(CodePurpose.PasswordReset);

        // Act
        var wrongPurpose = await Redeemer().RedeemAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, issued.Plaintext, default);
        var nobody = await Redeemer().RedeemAsync(Guid.NewGuid(), CodePurpose.PasswordReset, issued.Plaintext, default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(CodeOf(wrongPurpose)).IsEqualTo(IdentityErrorCodes.CodeInvalid);
            await Assert.That(CodeOf(nobody)).IsEqualTo(IdentityErrorCodes.CodeInvalid);
            await Assert.That(_limiter.Hits).IsEmpty();
            await Assert.That(_codes.Stored.Single().RedeemedAt).IsNull();
        }
    }
}
