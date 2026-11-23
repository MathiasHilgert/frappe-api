using Frappe.Messaging.Hosting;
using Frappe.Messaging.JetStream;
using Frappe.Messaging.Relay;
using Frappe.Messaging.Retention;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NATS.Client.Core;
using NodaTime;

namespace Frappe.Platform.UnitTests.Messaging;

/// <summary>The names, headers, options and failures the messaging platform builds everything else from.</summary>
public sealed class MessagingPrimitivesTests
{
    private static OutgoingMessage Outgoing(string? traceParent = null, string? traceState = null) =>
        new(
            Guid.NewGuid(),
            "frappe.test.something_happened.v1",
            "/frappe/test",
            "application/json",
            new byte[] { 1 },
            Instant.FromUtc(2026, 11, 16, 12, 0),
            traceParent,
            traceState);

    [Test]
    public async Task Without_a_namespace_the_names_are_the_production_names()
    {
        // Arrange
        var naming = JetStreamNaming.Default;

        // Act
        var names = (naming.Stream("orders"), naming.ModuleSubjects("orders"), naming.DeadLetterStream, naming.DeadLetterSubjects);

        // Assert
        await Assert.That(names).IsEqualTo(("FRAPPE_ORDERS", "frappe.orders.>", "FRAPPE_DLQ", "frappe.dlq.>"));
    }

    [Test]
    public async Task A_namespace_prefixes_every_stream_and_subject_but_not_the_event_type()
    {
        // Arrange
        var naming = new JetStreamNaming("t1a2b");

        // Act
        var names = (
            naming.Stream("orders"),
            naming.Subject("frappe.orders.order_placed.v1"),
            naming.DeadLetterSubject("orders", "c"),
            naming.DeadLetterStream);

        // Assert
        await Assert.That(names).IsEqualTo(
            ("T1A2B_FRAPPE_ORDERS", "t1a2b.frappe.orders.order_placed.v1", "t1a2b.frappe.dlq.orders.c", "T1A2B_FRAPPE_DLQ"));
    }

    [Test]
    [Arguments("UPPER")]
    [Arguments("with.dot")]
    [Arguments("space here")]
    public async Task A_namespace_that_is_not_one_lowercase_token_is_refused(string value)
    {
        // Arrange and Act
        var failure = Assert.Throws<ArgumentException>(() => _ = new JetStreamNaming(value));

        // Assert
        await Assert.That(failure.Message).Contains(value);
    }

    [Test]
    public async Task Naming_a_stream_or_subject_needs_a_name()
    {
        // Arrange
        var naming = JetStreamNaming.Default;

        // Act
        var stream = Assert.Throws<ArgumentException>(() => naming.Stream(" "));
        var subjects = Assert.Throws<ArgumentException>(() => naming.ModuleSubjects(""));
        var subject = Assert.Throws<ArgumentException>(() => naming.Subject(" "));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(stream.ParamName).IsEqualTo("module");
            await Assert.That(subjects.ParamName).IsEqualTo("module");
            await Assert.That(subject.ParamName).IsEqualTo("eventType");
        }
    }

    [Test]
    public async Task The_production_topology_helpers_use_the_default_names()
    {
        // Arrange and Act
        var names = (JetStreamTopology.StreamOf("orders"), JetStreamTopology.DeadLetterSubject("orders", "c"));

        // Assert
        await Assert.That(names).IsEqualTo(("FRAPPE_ORDERS", "frappe.dlq.orders.c"));
    }

    [Test]
    public async Task A_message_becomes_cloud_event_headers_with_its_trace_context_and_reads_back()
    {
        // Arrange
        var message = Outgoing("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01", "vendor=value");

        // Act
        var headers = CloudEventHeaders.From(message);
        var incoming = CloudEventHeaders.Read(headers, message.Data);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(headers[CloudEventHeaders.SpecVersion].ToString()).IsEqualTo("1.0");
            await Assert.That(headers[CloudEventHeaders.Source].ToString()).IsEqualTo("/frappe/test");
            await Assert.That(headers[CloudEventHeaders.ContentType].ToString()).IsEqualTo("application/json");
            await Assert.That(incoming.Id).IsEqualTo(message.Id);
            await Assert.That(incoming.Type).IsEqualTo(message.Type);
            await Assert.That(incoming.TraceParent).IsEqualTo(message.TraceParent);
            await Assert.That(incoming.TraceState).IsEqualTo("vendor=value");
        }
    }

    [Test]
    public async Task A_message_without_a_trace_context_carries_no_trace_headers()
    {
        // Arrange and Act
        var headers = CloudEventHeaders.From(Outgoing());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(headers.ContainsKey(CloudEventHeaders.TraceParent)).IsFalse();
            await Assert.That(headers.ContainsKey(CloudEventHeaders.TraceState)).IsFalse();
        }
    }

    [Test]
    public async Task A_received_message_without_a_valid_id_and_type_is_malformed()
    {
        // Arrange
        var noId = new NatsHeaders { [CloudEventHeaders.Type] = "frappe.test.x.v1" };
        var badId = new NatsHeaders { [CloudEventHeaders.Id] = "nope", [CloudEventHeaders.Type] = "frappe.test.x.v1" };

        // Act
        var withoutHeaders = Assert.Throws<FormatException>(() => CloudEventHeaders.Read(null, ReadOnlyMemory<byte>.Empty));
        var missingId = Assert.Throws<FormatException>(() => CloudEventHeaders.Read(noId, ReadOnlyMemory<byte>.Empty));
        var invalidId = Assert.Throws<FormatException>(() => CloudEventHeaders.Read(badId, ReadOnlyMemory<byte>.Empty));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(withoutHeaders.Message).Contains("not a Frappe CloudEvent");
            await Assert.That(missingId.Message).Contains("not a Frappe CloudEvent");
            await Assert.That(invalidId.Message).Contains("'nope'");
        }
    }

    [Test]
    public async Task The_rejected_exception_keeps_its_message_and_cause()
    {
        // Arrange
        var cause = new InvalidOperationException("inner");

        // Act
        var empty = new MessageRejectedException();
        var withMessage = new MessageRejectedException("too large");
        var withCause = new MessageRejectedException("too large", cause);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(empty.InnerException).IsNull();
            await Assert.That(withMessage.Message).IsEqualTo("too large");
            await Assert.That(withCause.InnerException).IsSameReferenceAs(cause);
        }
    }

    [Test]
    public async Task The_retention_is_a_whole_number_of_days_and_the_defaults_are_valid()
    {
        // Arrange
        var options = new MessageRetentionOptions { OutboxDays = 3, InboxDays = 9 };

        // Act
        var results = new MessageRetentionOptions().Validate(new(new object()));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(options.OutboxRetention).IsEqualTo(TimeSpan.FromDays(3));
            await Assert.That(options.InboxRetention).IsEqualTo(TimeSpan.FromDays(9));
            await Assert.That(results).IsEmpty();
        }
    }

    [Test]
    public async Task A_retention_outside_its_range_is_rejected_per_key()
    {
        // Arrange
        var options = new MessageRetentionOptions { OutboxDays = 0, InboxDays = 1, BatchSize = 0 };

        // Act
        var keys = options.Validate(new(new object())).SelectMany(result => result.MemberNames).ToList();

        // Assert
        await Assert.That(keys).IsEquivalentTo(["OutboxDays", "InboxDays", "BatchSize"]);
    }

    [Test]
    public async Task The_purge_result_holds_the_rows_each_table_lost()
    {
        // Arrange and Act
        var result = new MessagePurgeResult(2, 5);

        // Assert
        await Assert.That((result.OutboxRows, result.InboxRows)).IsEqualTo((2, 5));
    }

    [Test]
    public async Task The_nats_health_check_reports_the_registrations_failure_status_when_the_broker_does_not_answer()
    {
        // Arrange
        await using var connection = new NatsConnection(new NatsOpts { Url = "nats://127.0.0.1:1", ConnectTimeout = TimeSpan.FromSeconds(1) });
        var check = new NatsHealthCheck(connection);
        var context = new HealthCheckContext { Registration = new HealthCheckRegistration("nats", check, HealthStatus.Degraded, null) };

        // Act
        var result = await check.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
            await Assert.That(result.Description).IsEqualTo("NATS does not answer.");
            await Assert.That(result.Exception).IsNotNull();
        }
    }
}
