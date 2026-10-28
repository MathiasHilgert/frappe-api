using System.Text.Json;
using Alba;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Contracts;
using Frappe.Platform.Messaging;
using Frappe.TestUtilities;
using Microsoft.Extensions.Hosting;
using Wolverine.Nats.Internal;
using Wolverine.Tracking;
using BillingConsumer = Frappe.Platform.IntegrationTests.Messaging.Billing.Application.TicketOpenedConsumer;

namespace Frappe.Platform.IntegrationTests.Messaging;

// What reaches the application from outside its control: a stream nobody provisioned, a message nobody typed.
public sealed class NatsBoundaryTests(NatsFixture fixture) : IClassFixture<NatsFixture>
{
    private const string ForTheId = "position(convert_to($1::text, 'UTF8') in body) > 0";

    private readonly Guid _ticket = Guid.CreateVersion7();
    private readonly NatsScope _scope = NatsScope.On(fixture.Server);

    // A publishing role only logs a failed Verify, so readiness is what shows a stream nobody provisioned.
    [Fact]
    public async Task A_deployed_api_reports_nats_degraded_while_its_stream_is_missing()
    {
        await using IAlbaHost api = await fixture.StartHostAsync(
            _scope,
            Environments.Production,
            "api"
        );
        await api.GetRuntime()
            .Options.Transports.GetOrCreate<NatsTransport>()
            .Connection.ConnectAsync();

        await api.Scenario(static scenario =>
        {
            scenario.Get.Url("/health/ready");
            scenario.StatusCodeShouldBeOk();
            scenario.ContentShouldBe("Degraded");
        });
    }

    // Only a publisher other than the application omits Wolverine's message-type header. The subject names the
    // event, so the message is read as that event and, carrying no tenant, dead-lettered instead of dropped.
    [Fact]
    public async Task A_message_without_a_message_type_on_an_event_subject_is_dead_lettered()
    {
        await using IAlbaHost host = await fixture.StartHostAsync(_scope, Environments.Development);

        await JetStreamReads.PublishAsync(
            _scope,
            NatsTopology.Subject(_scope.Root, typeof(TicketOpened)),
            JsonSerializer.SerializeToUtf8Bytes(new TicketOpened(_ticket))
        );

        long deadLetters = await fixture.WaitForRowsAsync(
            $"SELECT count(*) FROM wolverine.wolverine_dead_letters WHERE {ForTheId}",
            _ticket
        );

        Runs.Of(_ticket, BillingConsumer.Name).ShouldBe(0);
        deadLetters.ShouldBeGreaterThan(0L);
    }
}
