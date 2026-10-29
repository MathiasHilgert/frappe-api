using System.Diagnostics;
using Frappe.Kernel.Messaging;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Application;
using Frappe.Platform.Messaging;
using Frappe.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Wolverine;
using Wolverine.Tracking;

namespace Frappe.Platform.IntegrationTests.Messaging;

[Collection(MessagingTestGroup.Name)]
public sealed class TelemetryTests(MessagingFixture fixture)
{
    // A Platform concern, tested here to reuse the messaging fixture's host and exporters.
    [Fact]
    public void Every_signal_names_the_service_its_environment_and_its_role()
    {
        Resource resource = fixture
            .Host.Services.GetRequiredService<TracerProvider>()
            .GetResource();

        var attributes = resource.Attributes.ToDictionary(
            static attribute => attribute.Key,
            static attribute => attribute.Value,
            StringComparer.Ordinal
        );
        attributes["service.name"].ShouldBe("frappe");
        attributes["deployment.environment.name"].ShouldBe("Development");
        attributes["frappe.role"].ShouldBe("all");
        attributes.ShouldContainKey("service.version");
        attributes.ShouldContainKey("service.instance.id");
    }

    [Fact]
    public async Task A_rejected_command_tags_its_span_with_the_error_code_and_keeps_it_ok()
    {
        ITrackedSession session = await fixture.Host.TrackAsync(
            Guid.CreateVersion7(),
            static async services =>
                await services
                    .GetRequiredService<IDispatcher>()
                    .EnqueueAsync(
                        new QueueTicket(Guid.CreateVersion7(), "reject"),
                        TestContext.Current.CancellationToken
                    )
        );

        Envelope handled = session.Executed.SingleEnvelope<QueueTicket>();
        // Wolverine's execution span, named after the message type; it may stop just after the session ends.
        Activity span = await SpanAsync(activity =>
            string.Equals(activity.DisplayName, handled.MessageType, StringComparison.Ordinal)
            && string.Equals(
                activity.GetTagItem("messaging.message_id")?.ToString(),
                handled.Id.ToString(),
                StringComparison.Ordinal
            )
        );
        span.GetTagItem(ErrorOrOutcome.ErrorCodeTag).ShouldBe("kitchen.ticket.rejected");
        span.Status.ShouldBe(ActivityStatusCode.Ok);
    }

    // Wolverine polls its store all the time; a poll outside any span must not start a trace of its own. Commands
    // under a span still trace, so the check is not satisfied by tracing nothing. Only the host's data source
    // (Application Name frappe-<role>) is filtered; the fixture's owner reads are not.
    [Fact]
    public async Task Database_commands_trace_only_under_another_span()
    {
        await fixture.Host.TrackAsync(
            Guid.CreateVersion7(),
            static async services =>
                await services
                    .GetRequiredService<IDispatcher>()
                    .EnqueueAsync(
                        new QueueTicket(Guid.CreateVersion7(), "ok"),
                        TestContext.Current.CancellationToken
                    )
        );

        await SpanAsync(static span => FromHost(span) && span.Parent is not null);
        fixture.Spans.ShouldNotContain(static span => FromHost(span) && span.Parent == null);
    }

    private static bool FromHost(Activity span) =>
        string.Equals(span.Source.Name, "Npgsql", StringComparison.Ordinal)
        && (span.GetTagItem("db.npgsql.data_source") as string ?? "").Contains(
            "Application Name=frappe-",
            StringComparison.Ordinal
        );

    private async Task<Activity> SpanAsync(Func<Activity, bool> match)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            if (fixture.Spans.FirstOrDefault(match) is { } span)
            {
                return span;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("No exported span matched within 5 s.");
    }
}
