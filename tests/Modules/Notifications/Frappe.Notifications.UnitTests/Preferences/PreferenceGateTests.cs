using Frappe.Notifications.Application.Dispatching;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.UnitTests.Dispatching;

namespace Frappe.Notifications.UnitTests.Preferences;

/// <summary>The gate keeps an opt-out across a hash key rotation by probing the recipient under every active key (S9, N2-D2).</summary>
public sealed class PreferenceGateTests
{
    private static readonly string OtherKey = Convert.ToBase64String(Enumerable.Repeat((byte)2, 32).ToArray());

    private static readonly ChannelName Mail = TestChannels.Mail;

    private static ResolvedContact Contact(Frappe.Notifications.Infrastructure.Addresses.HmacAddressHasher hasher, string address)
    {
        var hash = hasher.Hash(address);
        return new ResolvedContact(new ContactRecipient(Mail, address), RecipientKey.Of(hash), hash);
    }

    [Test]
    public async Task An_opt_out_made_under_the_old_hash_key_still_holds_after_the_key_rotated()
    {
        // Arrange
        var before = TestHasher.Create();
        var preferences = new FakePreferences();
        preferences.OptedOut.Add(RecipientKey.Of(before.Hash("ana@example.com")).Value);
        var rotated = TestHasher.Create(2, (2, OtherKey));
        var gate = new PreferenceGate(preferences, rotated);

        // Act
        var allowed = await gate.AllowsAsync(Contact(rotated, "Ana@Example.com"), NotificationCategory.Marketing, CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(allowed).IsFalse();
            await Assert.That(preferences.Probed.Single().Select(key => key.Value.Split(':')[1])).IsEquivalentTo(["1", "2"]);
        }
    }

    [Test]
    public async Task A_recipient_who_did_not_opt_out_passes_under_every_key()
    {
        // Arrange
        var rotated = TestHasher.Create(2, (2, OtherKey));
        var gate = new PreferenceGate(new FakePreferences(), rotated);

        // Act
        var allowed = await gate.AllowsAsync(Contact(rotated, "ana@example.com"), NotificationCategory.Marketing, CancellationToken.None);

        // Assert
        await Assert.That(allowed).IsTrue();
    }

    [Test]
    public async Task A_recipient_known_by_reference_is_probed_under_that_one_key()
    {
        // Arrange
        var hasher = TestHasher.Create(2, (2, OtherKey));
        var preferences = new FakePreferences();
        preferences.OptedOut.Add("ref:account:42");
        var gate = new PreferenceGate(preferences, hasher);
        var key = RecipientKey.Parse("ref:account:42").Value;
        var contact = new ResolvedContact(new ContactRecipient(Mail, "ana@example.com"), key, hasher.Hash("ana@example.com"));

        // Act
        var allowed = await gate.AllowsAsync(contact, NotificationCategory.Marketing, CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(allowed).IsFalse();
            await Assert.That(preferences.Probed.Single().Select(k => k.Value)).IsEquivalentTo(["ref:account:42"]);
        }
    }

    [Test]
    [Arguments(NotificationCategory.Security)]
    [Arguments(NotificationCategory.Transactional)]
    public async Task A_category_that_cannot_be_turned_off_passes_without_reading_preferences(NotificationCategory category)
    {
        // Arrange
        var hasher = TestHasher.Create();
        var preferences = new FakePreferences();
        preferences.OptedOut.Add(RecipientKey.Of(hasher.Hash("ana@example.com")).Value);
        var gate = new PreferenceGate(preferences, hasher);

        // Act
        var allowed = await gate.AllowsAsync(Contact(hasher, "ana@example.com"), category, CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(allowed).IsTrue();
            await Assert.That(preferences.Probed).IsEmpty();
        }
    }
}
