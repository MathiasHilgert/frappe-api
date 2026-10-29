using System.Diagnostics;
using Frappe.Kernel.Messaging;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Application;
using Frappe.Platform.Messaging;
using Frappe.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Wolverine.Tracking;

namespace Frappe.Platform.IntegrationTests.Messaging;

[Collection(MessagingTestGroup.Name)]
public sealed class TelemetryTests(MessagingFixture fixture)
{
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
    }

    // An expected error is an outcome, not a failure: Wolverine closes the message's span Ok, and the tag says which.
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

        string handled = session.Executed.SingleEnvelope<QueueTicket>().Id.ToString();
        Activity span = fixture.Spans.Single(activity =>
            string.Equals(
                activity.GetTagItem("messaging.message_id")?.ToString(),
                handled,
                StringComparison.Ordinal
            ) && activity.GetTagItem(ErrorOrOutcome.ErrorCodeTag) is not null
        );
        span.GetTagItem(ErrorOrOutcome.ErrorCodeTag).ShouldBe("kitchen.ticket.rejected");
        span.Status.ShouldBe(ActivityStatusCode.Ok);
    }

    // Wolverine polls its store all the time; a poll outside any span must not start a trace of its own. Only the
    // host's data source (Application Name frappe-<role>) is filtered; the fixture's owner reads are not.
    [Fact]
    public void Database_commands_trace_only_under_another_span()
    {
        fixture.Spans.ShouldNotContain(static span =>
            string.Equals(span.Source.Name, "Npgsql", StringComparison.Ordinal)
            && span.Parent == null
            && (span.GetTagItem("db.npgsql.data_source") as string ?? "").Contains(
                "Application Name=frappe-",
                StringComparison.Ordinal
            )
        );
    }
}
