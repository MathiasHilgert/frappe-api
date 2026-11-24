using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.UnitTests.Preferences;

/// <summary>Unsubscribe tokens are opaque, signed, never expire, rotate by key id and refuse everything that is not ours (S5, S12, S14).</summary>
public sealed class UnsubscribeTokensTests
{
    private const string Ref = "ref:account:42";

    private static readonly string Address = $"addr:1:{new string('a', 64)}";

    private static RecipientKey Key(string text) => RecipientKey.Parse(text).Value;

    [Test]
    [Arguments(Ref)]
    [Arguments("addr:1:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task A_token_reads_back_the_recipient_and_the_category_it_was_made_for(string recipient)
    {
        // Arrange
        var tokens = TestUnsubscribeTokens.Create();

        // Act
        var claim = tokens.Verify(tokens.Issue(Key(recipient), NotificationCategory.Marketing));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(claim.IsSuccess).IsTrue();
            await Assert.That(claim.Value.Recipient.Value).IsEqualTo(recipient);
            await Assert.That(claim.Value.Category).IsEqualTo(NotificationCategory.Marketing);
        }
    }

    [Test]
    public async Task The_same_recipient_and_category_always_give_the_same_token_so_a_mail_renders_alike_on_every_attempt()
    {
        // Arrange
        var tokens = TestUnsubscribeTokens.Create();

        // Act
        var first = tokens.Issue(Key(Ref), NotificationCategory.Marketing);
        var second = TestUnsubscribeTokens.Create().Issue(Key(Ref), NotificationCategory.Marketing);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(second).IsEqualTo(first);
            await Assert.That(tokens.Issue(Key("ref:account:43"), NotificationCategory.Marketing)).IsNotEqualTo(first);
        }
    }

    [Test]
    public async Task A_token_is_url_safe_opaque_and_carries_the_key_id()
    {
        // Arrange
        var tokens = TestUnsubscribeTokens.Create(7, retired: (1, TestUnsubscribeTokens.KeyTwo));

        // Act
        var token = tokens.Issue(Key(Ref), NotificationCategory.Marketing);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(token).StartsWith("7.");
            await Assert.That(token.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')).IsTrue();
            await Assert.That(token).DoesNotContain(Ref);
        }
    }

    [Test]
    public async Task A_security_or_transactional_category_has_no_token()
    {
        // Arrange
        var tokens = TestUnsubscribeTokens.Create();

        // Act
        var security = Assert.Throws<ArgumentException>(() => tokens.Issue(Key(Ref), NotificationCategory.Security));
        var transactional = Assert.Throws<ArgumentException>(() => tokens.Issue(Key(Ref), NotificationCategory.Transactional));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(security).IsNotNull();
            await Assert.That(transactional).IsNotNull();
        }
    }

    [Test]
    public async Task A_token_changed_in_any_part_is_refused_with_the_one_code()
    {
        // Arrange
        var tokens = TestUnsubscribeTokens.Create();
        var parts = tokens.Issue(Key(Ref), NotificationCategory.Marketing).Split('.');
        var otherPayload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("marketing\nref:account:43"));
        string[] tampered =
        [
            $"{parts[0]}.{otherPayload}.{parts[2]}",
            $"{parts[0]}.{parts[1]}.{parts[2][..^2]}AA",
            $"{parts[0]}.{parts[1]}.{Base64Url.EncodeToString(new byte[32])}",
            $"{parts[0]}.{parts[1]}",
            $"{parts[0]}.{parts[1]}.{parts[2]}.x",
        ];

        // Act
        var results = tampered.Select(tokens.Verify).ToArray();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(results.All(r => !r.IsSuccess)).IsTrue();
            await Assert.That(results.SelectMany(r => r.Errors).Select(e => e.Code).Distinct())
                .IsEquivalentTo(["notifications.unsubscribe_token.invalid"]);
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("garbage")]
    [Arguments("1..")]
    [Arguments("x.y.z")]
    [Arguments("99999999999.a.b")]
    [Arguments("-1.a.b")]
    [Arguments("1.!!!.???")]
    public async Task Text_that_is_not_a_token_is_refused_with_the_one_code_and_never_throws(string? token)
    {
        // Act
        var result = TestUnsubscribeTokens.Create().Verify(token);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Errors.Single().Code).IsEqualTo("notifications.unsubscribe_token.invalid");
        }
    }

    [Test]
    public async Task A_token_beyond_the_longest_accepted_is_refused_before_it_is_read()
    {
        // Arrange
        var tokens = TestUnsubscribeTokens.Create();
        var long_ = "1." + new string('A', 600) + "." + new string('A', 43);

        // Act
        var result = tokens.Verify(long_);

        // Assert
        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task A_token_signed_under_another_key_or_naming_an_unknown_key_id_is_refused()
    {
        // Arrange
        var mine = TestUnsubscribeTokens.Create();
        var foreign = TestUnsubscribeTokens.Create(key: TestUnsubscribeTokens.KeyTwo).Issue(Key(Ref), NotificationCategory.Marketing);
        var unknownId = TestUnsubscribeTokens.Create(5, TestUnsubscribeTokens.KeyTwo).Issue(Key(Ref), NotificationCategory.Marketing);

        // Act
        var sameIdOtherKey = mine.Verify(foreign);
        var otherId = mine.Verify(unknownId);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(sameIdOtherKey.IsSuccess).IsFalse();
            await Assert.That(otherId.IsSuccess).IsFalse();
        }
    }

    [Test]
    public async Task After_a_rotation_a_link_already_mailed_under_the_old_key_still_works_and_new_ones_use_the_new_key()
    {
        // Arrange
        var before = TestUnsubscribeTokens.Create();
        var old = before.Issue(Key(Ref), NotificationCategory.Marketing);
        var rotated = TestUnsubscribeTokens.Create(2, TestUnsubscribeTokens.KeyTwo, (1, TestUnsubscribeTokens.KeyOne));

        // Act
        var oldClaim = rotated.Verify(old);
        var fresh = rotated.Issue(Key(Ref), NotificationCategory.Marketing);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(oldClaim.IsSuccess).IsTrue();
            await Assert.That(oldClaim.Value.Recipient.Value).IsEqualTo(Ref);
            await Assert.That(fresh).StartsWith("2.");
            await Assert.That(rotated.Verify(fresh).IsSuccess).IsTrue();
            await Assert.That(before.Verify(fresh).IsSuccess).IsFalse();
        }
    }

    [Test]
    public async Task A_correctly_signed_payload_that_asks_for_a_category_that_cannot_be_turned_off_or_names_no_recipient_is_refused()
    {
        // Arrange
        var tokens = TestUnsubscribeTokens.Create();
        string[] payloads = ["security\n" + Ref, "transactional\n" + Ref, "marketing\nnot a key", "marketing", "marketing\n" + Ref + "\n"];

        // Act
        var results = payloads.Select(payload => tokens.Verify(Signed(payload))).ToArray();

        // Assert
        await Assert.That(results.All(r => !r.IsSuccess)).IsTrue();
    }

    [Test]
    public async Task A_correctly_signed_payload_of_the_documented_shape_is_accepted_which_pins_the_format()
    {
        // Act
        var claim = TestUnsubscribeTokens.Create().Verify(Signed("marketing\n" + Address));

        // Assert
        await Assert.That(claim.Value.Recipient.Value).IsEqualTo(Address);
    }

    [Test]
    public async Task A_mac_made_for_anything_but_a_token_never_verifies_as_one()
    {
        // Arrange
        var payload = Encoding.UTF8.GetBytes("marketing\n" + Ref);
        var mac = HMACSHA256.HashData(Convert.FromBase64String(TestUnsubscribeTokens.KeyOne), payload);
        var token = $"1.{Base64Url.EncodeToString(payload)}.{Base64Url.EncodeToString(mac)}";

        // Act
        var result = TestUnsubscribeTokens.Create().Verify(token);

        // Assert
        await Assert.That(result.IsSuccess).IsFalse();
    }

    /// <summary>A token for <paramref name="payload"/> signed the documented way with key 1, as only the holder of the key can.</summary>
    private static string Signed(string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var message = Encoding.UTF8.GetBytes("frappe.notifications.unsubscribe.v1\n1\n").Concat(bytes).ToArray();
        var mac = HMACSHA256.HashData(Convert.FromBase64String(TestUnsubscribeTokens.KeyOne), message);
        return $"1.{Base64Url.EncodeToString(bytes)}.{Base64Url.EncodeToString(mac)}";
    }
}
