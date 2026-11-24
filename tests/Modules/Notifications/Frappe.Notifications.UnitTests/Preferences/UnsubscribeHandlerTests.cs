using Frappe.Notifications.Application.Requests.Unsubscribe;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.Preferences;
using Frappe.Notifications.Infrastructure.Unsubscribe;
using Frappe.Notifications.UnitTests.Dispatching;
using NodaTime;

namespace Frappe.Notifications.UnitTests.Preferences;

/// <summary>The unsubscribe turns marketing off for the recipient a signed token names, once, and nothing else (S5, S9).</summary>
public sealed class UnsubscribeHandlerTests
{
    private static readonly RecipientKey Ana = RecipientKey.Parse("ref:account:42").Value;

    private static readonly Instant Now = Instant.FromUtc(2026, 11, 24, 10, 0);

    private static (UnsubscribeHandler Handler, FakePreferences Preferences) Build(HmacUnsubscribeTokens? tokens = null)
    {
        var preferences = new FakePreferences();
        return (new UnsubscribeHandler(tokens ?? TestUnsubscribeTokens.Create(), preferences, new FakeClock(Now)), preferences);
    }

    [Test]
    public async Task A_valid_token_stages_the_marketing_opt_out_of_its_recipient()
    {
        // Arrange
        var tokens = TestUnsubscribeTokens.Create();
        var (handler, preferences) = Build(tokens);

        // Act
        var result = await handler.HandleAsync(new Unsubscribe(tokens.Issue(Ana, NotificationCategory.Marketing)), CancellationToken.None);

        // Assert
        var staged = preferences.Staged.Single();
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(staged.RecipientKey).IsEqualTo("ref:account:42");
            await Assert.That(staged.Category).IsEqualTo(NotificationCategory.Marketing);
            await Assert.That(staged.OptedOutAt).IsEqualTo(Now);
            await Assert.That(staged.DomainEvents.OfType<PreferenceOptedOut>().Single().Category).IsEqualTo(NotificationCategory.Marketing);
        }
    }

    [Test]
    public async Task Doing_it_again_succeeds_and_stages_nothing()
    {
        // Arrange
        var tokens = TestUnsubscribeTokens.Create();
        var (handler, preferences) = Build(tokens);
        var command = new Unsubscribe(tokens.Issue(Ana, NotificationCategory.Marketing));
        await handler.HandleAsync(command, CancellationToken.None);

        // Act
        var again = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(again.IsSuccess).IsTrue();
            await Assert.That(preferences.Staged).HasSingleItem();
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("1.AAAA.BBBB")]
    public async Task A_token_that_is_not_ours_is_refused_before_the_store_is_read(string? token)
    {
        // Arrange
        var (handler, preferences) = Build();

        // Act
        var result = await handler.HandleAsync(new Unsubscribe(token), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Errors.Single().Code).IsEqualTo("notifications.unsubscribe_token.invalid");
            await Assert.That(preferences.Probed).IsEmpty();
            await Assert.That(preferences.Staged).IsEmpty();
        }
    }

    [Test]
    public async Task The_command_prints_no_token()
    {
        // Act
        var text = new Unsubscribe("1.secret.secret").ToString();

        // Assert
        await Assert.That(text).DoesNotContain("secret");
    }

    /// <summary>A clock stopped at one instant.</summary>
    private sealed class FakeClock(Instant now) : IClock
    {
        public Instant GetCurrentInstant() => now;
    }
}
