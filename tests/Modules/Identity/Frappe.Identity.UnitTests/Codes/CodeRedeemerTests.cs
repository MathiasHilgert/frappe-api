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
            await Assert.That(first.Value)
                .IsEqualTo(new RedeemedCode(issued.Id, CodePurpose.EmailVerification, "ana@example.com", CodeFixtures.Principal));
            await Assert.That(_codes.Stored.Single().RedeemedAt).IsEqualTo(Instant.FromUtc(2026, 11, 24, 12, 2));
            await Assert.That(CodeOf(second)).IsEqualTo(IdentityErrorCodes.CodeInvalid);
        }
    }

    [Test]
    public async Task A_guess_takes_a_principal_slot_and_then_a_slot_keyed_by_the_code_id_both_fail_closed()
    {
        // Arrange
        var issued = await IssueAsync();

        // Act
        await Redeemer().RedeemAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, "000000", default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(_limiter.Hits.Select(h => h.Flow)).IsEquivalentTo([CodeLimits.GuessPerPrincipal, CodeLimits.Guess]);
            await Assert.That(_limiter.Hits[0].Subject).IsEqualTo($"EmailVerification:{CodeFixtures.Principal:N}");
            await Assert.That(_limiter.Hits[1].Subject).IsEqualTo(issued.Id.ToString("N"));
            await Assert.That(CodeLimits.GuessPerPrincipal.Name).IsEqualTo("code.guess.principal");
            await Assert.That(CodeLimits.GuessPerPrincipal.Limit).IsEqualTo(10);
            await Assert.That(CodeLimits.GuessPerPrincipal.Window).IsEqualTo(TimeSpan.FromHours(24));
            await Assert.That(CodeLimits.GuessPerPrincipal.OnFailure).IsEqualTo(LimitFailure.FailClosed);
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

    [Test]
    public async Task The_principal_slot_is_taken_before_the_code_slot_and_a_spent_cap_leaves_the_code_slot_alone()
    {
        // Arrange
        var issued = await IssueAsync();
        _limiter.AnswerFor = flow => flow == CodeLimits.GuessPerPrincipal ? AttemptVerdict.Refused(TimeSpan.FromHours(3)) : AttemptVerdict.Allowed;

        // Act
        var result = await Redeemer().RedeemAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, issued.Plaintext, default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(CodeOf(result)).IsEqualTo(IdentityErrorCodes.CodeInvalid);
            await Assert.That(result.Errors.Single()).IsNotTypeOf<RateLimitedError>();
            await Assert.That(_limiter.Hits.Select(h => h.Flow)).IsEquivalentTo([CodeLimits.GuessPerPrincipal]);
            await Assert.That(_codes.Stored.Single().RedeemedAt).IsNull();
        }
    }

    [Test]
    public async Task While_the_principal_cap_cannot_be_counted_no_guess_is_judged()
    {
        // Arrange
        var issued = await IssueAsync();
        _limiter.AnswerFor = flow => flow == CodeLimits.GuessPerPrincipal ? AttemptVerdict.Unavailable : AttemptVerdict.Allowed;

        // Act
        var result = await Redeemer().RedeemAsync(CodeFixtures.Principal, CodePurpose.EmailVerification, issued.Plaintext, default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(CodeOf(result)).IsEqualTo(IdentityErrorCodes.AttemptsUnavailable);
            await Assert.That(_codes.Stored.Single().RedeemedAt).IsNull();
        }
    }

    [Test]
    public async Task Redeeming_by_id_takes_the_slot_of_the_id_before_the_code_is_looked_up_and_then_the_principal_slot()
    {
        // Arrange
        var issued = await IssueAsync(CodePurpose.PasswordReset);

        // Act
        var result = await Redeemer().RedeemByIdAsync(issued.Id.ToString(), CodePurpose.PasswordReset, issued.Plaintext, default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Value)
                .IsEqualTo(new RedeemedCode(issued.Id, CodePurpose.PasswordReset, "ana@example.com", CodeFixtures.Principal));
            await Assert.That(_limiter.Hits.Select(h => h.Flow)).IsEquivalentTo([CodeLimits.Guess, CodeLimits.GuessPerPrincipal]);
            await Assert.That(_limiter.Hits[0].Subject).IsEqualTo(issued.Id.ToString("N"));
            await Assert.That(_codes.Stored.Single().RedeemedAt).IsNotNull();
        }
    }

    [Test]
    public async Task An_id_nobody_was_given_costs_the_same_slot_and_answers_the_same_as_a_wrong_code()
    {
        // Arrange
        await IssueAsync(CodePurpose.PasswordReset);
        var decoy = Guid.CreateVersion7();

        // Act
        var result = await Redeemer().RedeemByIdAsync(decoy.ToString(), CodePurpose.PasswordReset, "123456", default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(CodeOf(result)).IsEqualTo(IdentityErrorCodes.CodeInvalid);
            await Assert.That(_limiter.Hits.Single()).IsEqualTo((CodeLimits.Guess, decoy.ToString("N")));
        }
    }

    [Test]
    [Arguments("not-an-id")]
    [Arguments("")]
    [Arguments(null)]
    public async Task A_malformed_id_takes_the_one_shared_slot_and_is_invalid(string? id)
    {
        // Act
        var result = await Redeemer().RedeemByIdAsync(id, CodePurpose.PasswordReset, "123456", default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(CodeOf(result)).IsEqualTo(IdentityErrorCodes.CodeInvalid);
            await Assert.That(_limiter.Hits.Single()).IsEqualTo((CodeLimits.Guess, "malformed"));
        }
    }

    [Test]
    public async Task A_down_counter_refuses_an_id_nobody_was_given_as_it_refuses_a_real_one()
    {
        // Arrange
        var issued = await IssueAsync(CodePurpose.PasswordReset);
        _limiter.Answer = AttemptVerdict.Unavailable;

        // Act
        var real = await Redeemer().RedeemByIdAsync(issued.Id.ToString(), CodePurpose.PasswordReset, issued.Plaintext, default);
        var decoy = await Redeemer().RedeemByIdAsync(Guid.CreateVersion7().ToString(), CodePurpose.PasswordReset, "123456", default);
        var malformed = await Redeemer().RedeemByIdAsync("junk", CodePurpose.PasswordReset, "123456", default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(CodeOf(real)).IsEqualTo(IdentityErrorCodes.AttemptsUnavailable);
            await Assert.That(CodeOf(decoy)).IsEqualTo(IdentityErrorCodes.AttemptsUnavailable);
            await Assert.That(CodeOf(malformed)).IsEqualTo(IdentityErrorCodes.AttemptsUnavailable);
            await Assert.That(_codes.Stored.Single().RedeemedAt).IsNull();
        }
    }

    [Test]
    public async Task A_superseded_id_is_invalid_even_with_the_text_of_the_newer_code_and_a_code_of_another_purpose_is_invalid()
    {
        // Arrange
        var old = await IssueAsync(CodePurpose.PasswordReset);
        var current = await IssueAsync(CodePurpose.PasswordReset);
        var verification = await IssueAsync();

        // Act
        var superseded = await Redeemer().RedeemByIdAsync(old.Id.ToString(), CodePurpose.PasswordReset, current.Plaintext, default);
        var wrongPurpose = await Redeemer().RedeemByIdAsync(verification.Id.ToString(), CodePurpose.PasswordReset, verification.Plaintext, default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(CodeOf(superseded)).IsEqualTo(IdentityErrorCodes.CodeInvalid);
            await Assert.That(CodeOf(wrongPurpose)).IsEqualTo(IdentityErrorCodes.CodeInvalid);
            await Assert.That(_codes.Stored.Single(c => c.Id == current.Id).RedeemedAt).IsNull();
            await Assert.That(_codes.Stored.Single(c => c.Id == verification.Id).RedeemedAt).IsNull();
        }
    }

    [Test]
    public async Task A_spent_principal_cap_makes_even_the_right_code_by_id_invalid_and_leaves_it_unspent()
    {
        // Arrange
        var issued = await IssueAsync(CodePurpose.PasswordReset);
        _limiter.AnswerFor = flow => flow == CodeLimits.GuessPerPrincipal ? AttemptVerdict.Refused(TimeSpan.FromHours(3)) : AttemptVerdict.Allowed;

        // Act
        var result = await Redeemer().RedeemByIdAsync(issued.Id.ToString(), CodePurpose.PasswordReset, issued.Plaintext, default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(CodeOf(result)).IsEqualTo(IdentityErrorCodes.CodeInvalid);
            await Assert.That(_codes.Stored.Single().RedeemedAt).IsNull();
        }
    }
}
