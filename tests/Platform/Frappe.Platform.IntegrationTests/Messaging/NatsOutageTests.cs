using Alba;
using Frappe.Kernel.Messaging;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Application;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Contracts;
using Frappe.Platform.Messaging;
using Frappe.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;
using Wolverine;
using Wolverine.Nats.Configuration;
using Wolverine.Nats.Internal;
using Wolverine.Runtime.Serialization;
using Wolverine.Tracking;
using BillingConsumer = Frappe.Platform.IntegrationTests.Messaging.Billing.Application.TicketOpenedConsumer;

namespace Frappe.Platform.IntegrationTests.Messaging;

[Collection(NatsOutageGroup.Name)]
public sealed class NatsOutageTests(NatsOutageFixture fixture)
{
    private const string Outbox =
        "SELECT count(*) FROM wolverine.wolverine_outgoing_envelopes WHERE position(convert_to($1::text, 'UTF8') in body) > 0";

    private readonly Guid _tenant = Guid.CreateVersion7();
    private readonly Guid _ticket = Guid.CreateVersion7();

    // The api role keeps taking requests while NATS is away: the outbox holds the event until NATS answers.
    [Fact]
    public async Task The_api_starts_without_nats_keeps_the_event_in_the_outbox_and_sends_it_when_nats_returns()
    {
        var scope = NatsScope.On(fixture.Server);
        await fixture.ProvisionAsync(scope);
        await fixture.Server.StopAsync();

        await using IAlbaHost api = await fixture.StartHostAsync(
            scope,
            Environments.Production,
            "api"
        );
        await InvokeAsync(api);
        (await fixture.CountAsync(Outbox, _ticket)).ShouldBe(1);
        // Ready, but degraded: the api keeps serving while NATS is away.
        await api.Scenario(static scenario =>
        {
            scenario.Get.Url("/health/ready");
            scenario.StatusCodeShouldBeOk();
            scenario.ContentShouldBe("Degraded");
        });

        await fixture.Server.StartAsync();
        await using IAlbaHost _ = await fixture.StartHostAsync(
            scope,
            Environments.Production,
            "worker"
        );
        await Runs.WaitForAsync(_ticket, BillingConsumer.Name);

        Runs.Of(_ticket, BillingConsumer.Name).ShouldBe(1);
        (await fixture.CountAsync(Outbox, _ticket)).ShouldBe(0);
    }

    // A running worker loses NATS: the committed event waits in the outbox and drains once NATS is back.
    [Fact]
    public async Task An_event_committed_while_nats_is_down_stays_in_the_outbox_and_drains_after_the_restart()
    {
        var scope = NatsScope.On(fixture.Server);
        await using IAlbaHost host = await fixture.StartHostAsync(
            scope,
            Environments.Development,
            configure: static builder =>
                builder.Services.ConfigureWolverine(static options =>
                    ShortenNatsClientTimeouts(options)
                )
        );
        await fixture.Server.StopAsync();

        await InvokeAsync(host);
        (await fixture.CountAsync(Outbox, _ticket)).ShouldBe(1);
        await fixture.Server.StartAsync();
        await Runs.WaitForAsync(_ticket, BillingConsumer.Name);

        (await fixture.CountAsync(Outbox, _ticket)).ShouldBe(0);
    }

    // An inbox write that fails leaves the event unacknowledged, and unlimited deliveries keep the server offering
    // it, so a PostgreSQL outage while NATS is up delays the event and never drops it. The test waits for one
    // redelivery, not for a finite budget to run out: a redelivery after the failed write plus MaxDeliver -1 on the
    // server is what rules out the skip, in about a minute instead of the old 5 x 30 s budget plus margin.
    [Fact]
    public async Task An_event_that_arrives_while_postgres_is_down_is_handled_once_after_it_returns()
    {
        var scope = NatsScope.On(fixture.Server);
        await fixture.ProvisionAsync(scope);
        await using IAlbaHost worker = await fixture.StartHostAsync(
            scope,
            Environments.Production,
            "worker"
        );
        string consumer = NatsTopology.Consumer(scope.Root, typeof(TicketOpened));
        IMessageSerializer serializer = worker.GetRuntime().Options.DefaultSerializer;
        NatsHeaders headers = new()
        {
            ["id"] = _ticket.ToString(),
            ["Nats-Msg-Id"] = _ticket.ToString(),
            ["message-type"] = "kitchen.ticket.opened.v1",
            ["content-type"] = serializer.ContentType,
            ["tenant-id"] = _tenant.ToString(),
        };

        await fixture.PauseDatabaseAsync();
        try
        {
            await JetStreamReads.PublishAsync(
                scope,
                NatsTopology.Subject(scope.Root, typeof(TicketOpened)),
                serializer.WriteMessage(new TicketOpened(_ticket)),
                headers
            );
            await JetStreamReads.WaitForRedeliveryAsync(scope, consumer);
        }
        finally
        {
            await fixture.UnpauseDatabaseAsync();
        }

        await Runs.WaitForAsync(_ticket, BillingConsumer.Name);
        Runs.Of(_ticket, BillingConsumer.Name).ShouldBe(1);
        ConsumerInfo info = await JetStreamReads.ConsumerAsync(scope, consumer);
        info.Config.MaxDeliver.ShouldBe(NatsTopology.MaxDeliver);
    }

    // A publish on a connection that lost NATS waits out the client's command timeout (one minute) and request
    // timeout before the outbox keeps the event; the test proves the outbox, not those waits.
    private static void ShortenNatsClientTimeouts(WolverineOptions options)
    {
        NatsTransportConfiguration configuration = options
            .Transports.GetOrCreate<NatsTransport>()
            .Configuration;
        Func<NatsOpts, NatsOpts>? configured = configuration.ConfigureNatsOpts;
        configuration.ConfigureNatsOpts = opts =>
            (configured?.Invoke(opts) ?? opts) with
            {
                CommandTimeout = TimeSpan.FromSeconds(2),
                RequestTimeout = TimeSpan.FromSeconds(2),
            };
    }

    private async Task InvokeAsync(IAlbaHost host)
    {
        using var scope = TenantScope.Begin(_tenant, branchId: null, TenantScope.SystemPrincipalId);
        await using AsyncServiceScope services = host.Services.CreateAsyncScope();
        ErrorOr<Guid> opened = await services
            .ServiceProvider.GetRequiredService<IDispatcher>()
            .InvokeAsync(new OpenTicket(_ticket, "ok"), TestContext.Current.CancellationToken);
        opened.Value.ShouldBe(_ticket);
    }
}
