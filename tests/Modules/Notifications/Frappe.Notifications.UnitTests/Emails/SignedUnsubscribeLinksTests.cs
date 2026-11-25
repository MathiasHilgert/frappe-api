using Frappe.Notifications.Application.Channels;
using Frappe.Notifications.Application.Preferences;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Senders.Email;
using Frappe.Notifications.UnitTests.Preferences;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>
/// A Marketing mail carries the signed one-click link in its headers and the signed page link in its body, and nothing else does (S5, S9), while
/// a host that cannot make them does not start (CFG-01).
/// </summary>
public sealed class SignedUnsubscribeLinksTests
{
    private static readonly RecipientKey Ana = RecipientKey.Parse("ref:account:42").Value;

    private static UnsubscribeLinkOptions Options(string page = "https://frappe.example/unsubscribe", string api = "https://api.frappe.example/") =>
        new() { PageUrl = new Uri(page), ApiBaseUrl = new Uri(api) };

    private static SignedUnsubscribeLinks Links(UnsubscribeLinkOptions options) =>
        new(TestUnsubscribeTokens.Create(), Microsoft.Extensions.Options.Options.Create(options));

    [Test]
    public async Task Both_links_carry_the_same_token_that_reads_back_the_recipient()
    {
        // Arrange
        var links = Links(Options());

        // Act
        var link = links.LinkFor(Ana, NotificationCategory.Marketing);

        // Assert
        var token = link.OneClickUrl.Query["?token=".Length..];
        var claim = TestUnsubscribeTokens.Create().Verify(token);
        using (Assert.Multiple())
        {
            await Assert.That(link.OneClickUrl.GetLeftPart(UriPartial.Path)).IsEqualTo("https://api.frappe.example/v1/notifications/unsubscriptions");
            await Assert.That(link.PageUrl.AbsoluteUri).IsEqualTo($"https://frappe.example/unsubscribe?token={token}");
            await Assert.That(claim.Value.Recipient).IsEqualTo(Ana);
            await Assert.That(claim.Value.Category).IsEqualTo(NotificationCategory.Marketing);
        }
    }

    [Test]
    public async Task The_page_keeps_its_own_query_and_the_api_may_sit_under_a_path()
    {
        // Arrange
        var links = Links(Options("https://frappe.example/unsubscribe?from=mail", "https://frappe.example/api/"));

        // Act
        var link = links.LinkFor(Ana, NotificationCategory.Marketing);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(link.PageUrl.Query).StartsWith("?from=mail&token=");
            await Assert.That(link.OneClickUrl.GetLeftPart(UriPartial.Path)).IsEqualTo("https://frappe.example/api/v1/notifications/unsubscriptions");
        }
    }

    [Test]
    public async Task The_links_are_the_same_on_every_attempt()
    {
        // Arrange
        var links = Links(Options());

        // Act
        var first = links.LinkFor(Ana, NotificationCategory.Marketing);
        var second = links.LinkFor(Ana, NotificationCategory.Marketing);

        // Assert
        await Assert.That(second).IsEqualTo(first);
    }

    [Test]
    public async Task A_marketing_mail_gets_the_signed_headers_and_the_signed_page_link()
    {
        // Arrange
        await using var world = new EmailWorld(services => services
            .RemoveAll<IEmailUnsubscribeLinks>()
            .AddSingleton<IUnsubscribeTokens>(TestUnsubscribeTokens.Create())
            .AddSingleton<IOptions<UnsubscribeLinkOptions>>(Microsoft.Extensions.Options.Options.Create(Options()))
            .AddSingleton<IEmailUnsubscribeLinks, SignedUnsubscribeLinks>());

        // Act
        var outcome = await world.Channel.DeliverAsync(world.ItemOf("product_announcement", "es-AR"), CancellationToken.None);

        // Assert
        var email = world.Sender.Sent.Single();
        var header = email.Headers["List-Unsubscribe"];
        var token = header[(header.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..^1];
        using (Assert.Multiple())
        {
            await Assert.That(outcome).IsTypeOf<ChannelOutcome.Sent>();
            await Assert.That(header).StartsWith("<https://api.frappe.example/v1/notifications/unsubscriptions?token=");
            await Assert.That(email.Headers["List-Unsubscribe-Post"]).IsEqualTo("List-Unsubscribe=One-Click");
            await Assert.That(email.Html).Contains($"href=\"https://frappe.example/unsubscribe?token={token}\"");
            await Assert.That(TestUnsubscribeTokens.Create().Verify(token).Value.Recipient.Value).IsEqualTo("ref:account:42");
        }
    }

    [Test]
    [Arguments("account_activation_code")]
    [Arguments("password_reset_code")]
    [Arguments("email_change_code")]
    public async Task A_security_mail_has_no_unsubscribe_headers_and_no_link(string kind)
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        await world.Channel.DeliverAsync(world.ItemOf(kind, "es-AR"), CancellationToken.None);

        // Assert
        var email = world.Sender.Sent.Single();
        using (Assert.Multiple())
        {
            await Assert.That(email.Headers).IsEmpty();
            await Assert.That(email.Html).DoesNotContain("<a ");
            await Assert.That(email.Text).DoesNotContain("http");
        }
    }

    [Test]
    public async Task The_options_are_valid_with_an_http_page_and_an_api_address_ending_in_a_slash()
    {
        // Act
        var result = new UnsubscribeLinkOptionsValidator().Validate(null, Options("http://localhost:5173/unsubscribe", "http://localhost:8080/"));

        // Assert
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [Arguments(null, "https://api.frappe.example/", "Notifications:Unsubscribe:PageUrl")]
    [Arguments("ftp://frappe.example/u", "https://api.frappe.example/", "Notifications:Unsubscribe:PageUrl")]
    [Arguments("https://frappe.example/u", null, "Notifications:Unsubscribe:ApiBaseUrl")]
    [Arguments("https://frappe.example/u", "https://api.frappe.example/api", "Notifications:Unsubscribe:ApiBaseUrl")]
    [Arguments("https://frappe.example/u", "mailto:a@b.c", "Notifications:Unsubscribe:ApiBaseUrl")]
    public async Task A_missing_or_unusable_address_fails_the_start_naming_the_key(string? page, string? api, string key)
    {
        // Arrange
        var options = new UnsubscribeLinkOptions
        {
            PageUrl = page is null ? null : new Uri(page),
            ApiBaseUrl = api is null ? null : new Uri(api),
        };

        // Act
        var result = new UnsubscribeLinkOptionsValidator().Validate(null, options);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(string.Join(' ', result.Failures!)).Contains(key);
        }
    }
}
