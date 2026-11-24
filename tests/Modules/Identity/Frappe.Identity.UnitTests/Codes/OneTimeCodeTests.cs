using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Codes;
using Frappe.Results;
using NodaTime;

namespace Frappe.Identity.UnitTests.Codes;

/// <summary>A code is single use, lives 15 minutes and is useless once replaced; what it keeps is only its keyed hash (S5).</summary>
public sealed class OneTimeCodeTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 24, 12, 0);
    private static readonly CodeHmac Hmac = CodeHmac.Create(new byte[32]).Value;

    private static OneTimeCode Issued() =>
        OneTimeCode.Issue(CodeFixtures.Principal, CodePurpose.EmailVerification, "ana@example.com", Hmac, Now, OneTimeCode.Lifetime);

    [Test]
    public async Task A_code_expires_fifteen_minutes_after_it_was_issued_and_holds_the_live_slot()
    {
        // Act
        var code = Issued();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(code.ExpiresAt).IsEqualTo(Now + Duration.FromMinutes(15));
            await Assert.That(code.IssuedAt).IsEqualTo(Now);
            await Assert.That(code.HoldsLiveSlot).IsTrue();
            await Assert.That(code.RedeemedAt).IsNull();
            await Assert.That(code.InvalidatedAt).IsNull();
        }
    }

    [Test]
    public async Task Redeeming_a_live_code_spends_it_once()
    {
        // Arrange
        var code = Issued();

        // Act
        var first = code.Redeem(Now + Duration.FromMinutes(1));
        var second = code.Redeem(Now + Duration.FromMinutes(2));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.IsSuccess).IsTrue();
            await Assert.That(code.RedeemedAt).IsEqualTo(Now + Duration.FromMinutes(1));
            await Assert.That(code.HoldsLiveSlot).IsFalse();
            await Assert.That(second.Errors.Single()).IsEqualTo(Error.RuleViolation(IdentityErrorCodes.CodeInvalid));
            await Assert.That(code.RedeemedAt).IsEqualTo(Now + Duration.FromMinutes(1));
        }
    }

    [Test]
    public async Task A_code_redeems_until_the_instant_it_expires_and_not_at_it()
    {
        // Arrange
        var lastMoment = Issued();
        var atExpiry = Issued();

        // Act
        var before = lastMoment.Redeem(Now + Duration.FromMinutes(15) - Duration.FromNanoseconds(1));
        var at = atExpiry.Redeem(Now + Duration.FromMinutes(15));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(before.IsSuccess).IsTrue();
            await Assert.That(at.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.CodeInvalid);
            await Assert.That(atExpiry.RedeemedAt).IsNull();
        }
    }

    [Test]
    public async Task An_invalidated_code_is_superseded_and_refuses_to_redeem()
    {
        // Arrange
        var code = Issued();

        // Act
        code.Invalidate(Now + Duration.FromMinutes(1));
        var result = code.Redeem(Now + Duration.FromMinutes(2));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(code.InvalidatedAt).IsEqualTo(Now + Duration.FromMinutes(1));
            await Assert.That(code.HoldsLiveSlot).IsFalse();
            await Assert.That(result.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.CodeInvalid);
            await Assert.That(code.RedeemedAt).IsNull();
        }
    }

    [Test]
    public async Task Invalidating_leaves_a_redeemed_or_already_invalidated_code_as_it_was()
    {
        // Arrange
        var redeemed = Issued();
        var invalidated = Issued();
        _ = redeemed.Redeem(Now + Duration.FromMinutes(1));
        invalidated.Invalidate(Now + Duration.FromMinutes(1));

        // Act
        redeemed.Invalidate(Now + Duration.FromMinutes(3));
        invalidated.Invalidate(Now + Duration.FromMinutes(3));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(redeemed.InvalidatedAt).IsNull();
            await Assert.That(invalidated.InvalidatedAt).IsEqualTo(Now + Duration.FromMinutes(1));
        }
    }

    [Test]
    public async Task An_expired_code_nobody_redeemed_still_holds_the_live_slot_until_a_newer_one_invalidates_it()
    {
        // Arrange
        var code = Issued();
        var later = Now + Duration.FromHours(2);

        // Act
        var redeemed = code.Redeem(later);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(code.IsLive(later)).IsFalse();
            await Assert.That(code.HoldsLiveSlot).IsTrue();
            await Assert.That(redeemed.IsSuccess).IsFalse();
        }
    }

    [Test]
    public async Task A_clock_behind_the_issue_instant_never_stamps_a_time_before_it()
    {
        // Arrange
        var code = Issued();
        var other = Issued();

        // Act
        code.Invalidate(Now - Duration.FromSeconds(2));
        _ = other.Redeem(Now - Duration.FromSeconds(2));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(code.InvalidatedAt).IsEqualTo(Now);
            await Assert.That(other.RedeemedAt).IsEqualTo(Now);
        }
    }

    [Test]
    public async Task A_hash_of_the_wrong_size_is_refused_and_a_hash_never_prints_its_bytes()
    {
        // Act
        var tooShort = CodeHmac.Create(new byte[31]);
        var printed = CodeHmac.Create(new byte[32]).Value.ToString();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(tooShort.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.CodeHmacLength);
            await Assert.That(printed).IsEqualTo("***");
            await Assert.That(CodeHmac.Create(new byte[32]).Value).IsEqualTo(CodeHmac.Create(new byte[32]).Value);
        }
    }
}
