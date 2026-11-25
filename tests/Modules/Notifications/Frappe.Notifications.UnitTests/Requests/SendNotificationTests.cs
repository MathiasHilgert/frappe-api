using System.Text.Json;
using Frappe.Notifications.Application.Messages;
using Frappe.Notifications.Application.Requests.SendNotification;
using Frappe.Notifications.Domain.Requests;
using Frappe.Notifications.Messages.Account;
using Frappe.Notifications.Messages.Marketing;
using Frappe.Notifications.Senders.Email;
using Frappe.Results;
using NodaTime;

namespace Frappe.Notifications.UnitTests.Requests;

/// <summary>
/// The explicit entry path (S2): <c>SendNotification</c> checks the kind and its payload against the catalog and accepts the request by
/// staging an aggregate whose event reaches the dispatch; nothing about the payload is stored (S13).
/// </summary>
public sealed class SendNotificationTests
{
    private static readonly Guid RequestId = Guid.Parse("0192b6f0-0000-7000-8000-0000000000aa");

    private static readonly IClock Clock = new FakeClock(Instant.FromUtc(2026, 11, 24, 10, 0));

    private static SendNotificationHandler HandlerOver(FakeSendRequests requests) =>
        new(
            new NotificationCatalog([
                NotificationKind.Of<AccountActivationCode, AccountActivationCodePayload>(),
                NotificationKind.Of<ProductAnnouncement, ProductAnnouncementPayload>()]),
            requests,
            Clock,
            [new EmailPayloadCheck(new EmailComposer(new IcuNotificationTexts(
            [
                new EmbeddedNotificationTextSource(typeof(AccountActivationCode).Assembly),
                new EmbeddedNotificationTextSource(typeof(ProductAnnouncement).Assembly),
            ])))]);

    private static JsonElement Json(string json) => JsonElement.Parse(json);

    private static string[] FieldsOf(Result<Unit> result) => [.. (IEnumerable<string>)result.Errors.Single().Parameters["fields"]!];

    [Test]
    public async Task A_known_kind_with_a_valid_payload_stages_one_request_that_raises_the_event_with_the_payload()
    {
        // Arrange
        var requests = new FakeSendRequests();
        var payload = Json("""{"email":"ana@example.com","language":"es-AR","name":"Ana","code":"482913"}""");

        // Act
        var result = await HandlerOver(requests).HandleAsync(new SendNotification(RequestId, "account_activation_code", payload), default);

        // Assert
        var accepted = requests.Staged.Single();
        var raised = (NotificationRequestAccepted)accepted.DomainEvents.Single();
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(accepted.Id).IsEqualTo(RequestId);
            await Assert.That(accepted.Kind).IsEqualTo("account_activation_code");
            await Assert.That(accepted.RequestedAt).IsEqualTo(Instant.FromUtc(2026, 11, 24, 10, 0));
            await Assert.That(raised.RequestId).IsEqualTo(RequestId);
            await Assert.That(raised.Payload.GetProperty("code").GetString()).IsEqualTo("482913");
        }
    }

    [Test]
    public async Task An_unknown_kind_is_refused_with_its_code_and_nothing_is_staged()
    {
        // Arrange
        var requests = new FakeSendRequests();

        // Act
        var result = await HandlerOver(requests).HandleAsync(new SendNotification(RequestId, "nope", Json("{}")), default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Errors.Single()).IsEqualTo(Error.InvalidValue("notifications.kind.unknown", ("kind", "nope")));
            await Assert.That(requests.Staged).IsEmpty();
        }
    }

    [Test]
    public async Task A_payload_missing_fields_is_refused_naming_every_missing_field()
    {
        // Arrange
        var requests = new FakeSendRequests();

        // Act
        var result = await HandlerOver(requests)
            .HandleAsync(new SendNotification(RequestId, "account_activation_code", Json("""{"language":"es-AR"}""")), default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Errors.Single().Code).IsEqualTo("notifications.payload.invalid");
            await Assert.That(FieldsOf(result)).IsEquivalentTo(["email", "name", "code"]);
            await Assert.That(requests.Staged).IsEmpty();
        }
    }

    [Test]
    public async Task A_payload_of_the_wrong_shape_or_with_an_unknown_field_is_refused_without_echoing_its_values()
    {
        // Arrange
        var requests = new FakeSendRequests();
        var handler = HandlerOver(requests);

        // Act
        var wrongType = await handler.HandleAsync(new SendNotification(RequestId, "account_activation_code", Json("""{"email":5}""")), default);
        var unknown = await handler.HandleAsync(
            new SendNotification(RequestId, "account_activation_code",
                Json("""{"email":"a@b.co","name":"A","code":"1","secret":"hunter2"}""")), default);
        var notAnObject = await handler.HandleAsync(new SendNotification(RequestId, "account_activation_code", Json("[1]")), default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(wrongType.Errors.Single().Code).IsEqualTo("notifications.payload.invalid");
            await Assert.That(FieldsOf(wrongType)).Contains("email");
            await Assert.That(unknown.Errors.Single().Code).IsEqualTo("notifications.payload.invalid");
            await Assert.That(FieldsOf(unknown)).Contains("secret");
            await Assert.That(notAnObject.Errors.Single().Code).IsEqualTo("notifications.payload.invalid");
            await Assert.That(requests.Staged).IsEmpty();
            await Assert.That(string.Join(' ', unknown.Errors.Select(e => e.ToString()))).DoesNotContain("hunter2");
        }
    }

    [Test]
    public async Task A_url_that_is_not_absolute_https_is_refused_at_the_request_naming_the_kind_and_nothing_is_staged()
    {
        // Arrange
        var requests = new FakeSendRequests();
        var payload = Json("""{"email":"a@b.co","name":"Ana","headline":"News","summary":"More","url":"http://frappe.example/news"}""");

        // Act
        var result = await HandlerOver(requests).HandleAsync(new SendNotification(RequestId, "product_announcement", payload), default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Errors.Single().Code).IsEqualTo("notifications.payload.invalid");
            await Assert.That(FieldsOf(result)).IsEquivalentTo(["payload"]);
            await Assert.That(requests.Staged).IsEmpty();
        }
    }

    [Test]
    [Arguments("Visit http://evil.example")]
    [Arguments("www.evil.example")]
    [Arguments("evil://x")]
    [Arguments("HTTP://x")]
    [Arguments("0123456789012345678901234567890123456789012345678901234567890")]
    public async Task A_name_that_is_too_long_or_looks_like_a_link_is_refused_naming_the_name_field(string name)
    {
        // Arrange
        var requests = new FakeSendRequests();
        var payload = Json($$"""{"email":"a@b.co","name":"{{name}}","code":"482913"}""");

        // Act
        var result = await HandlerOver(requests).HandleAsync(new SendNotification(RequestId, "account_activation_code", payload), default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Errors.Single().Code).IsEqualTo("notifications.payload.invalid");
            await Assert.That(FieldsOf(result)).IsEquivalentTo(["name"]);
            await Assert.That(requests.Staged).IsEmpty();
        }
    }

    [Test]
    public async Task A_name_of_exactly_sixty_characters_is_accepted()
    {
        // Arrange
        var requests = new FakeSendRequests();
        var payload = Json($$"""{"email":"a@b.co","name":"{{new string('n', 60)}}","code":"482913"}""");

        // Act
        var result = await HandlerOver(requests).HandleAsync(new SendNotification(RequestId, "account_activation_code", payload), default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(requests.Staged).HasSingleItem();
        }
    }

    [Test]
    public async Task A_request_already_accepted_succeeds_without_staging_it_again()
    {
        // Arrange
        var requests = new FakeSendRequests();
        requests.Known.Add(RequestId);
        var payload = Json("""{"email":"ana@example.com","language":null,"name":"Ana","code":"482913"}""");

        // Act
        var result = await HandlerOver(requests).HandleAsync(new SendNotification(RequestId, "account_activation_code", payload), default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(requests.Staged).IsEmpty();
        }
    }

    [Test]
    public async Task The_command_prints_no_payload()
    {
        // Arrange
        var command = new SendNotification(RequestId, "account_activation_code", Json("""{"code":"482913"}"""));

        // Act
        var text = command.ToString();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(text).DoesNotContain("482913");
            await Assert.That(text).Contains("account_activation_code");
        }
    }

    /// <summary>A clock that always reads the same instant.</summary>
    private sealed class FakeClock(Instant now) : IClock
    {
        public Instant GetCurrentInstant() => now;
    }
}
