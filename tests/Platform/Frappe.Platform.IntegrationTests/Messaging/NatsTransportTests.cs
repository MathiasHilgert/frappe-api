using Alba;
using Frappe.Kernel.Messaging;
using Frappe.Platform.IntegrationTests.Messaging.Billing.Infrastructure;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Application;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Contracts;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Infrastructure;
using Frappe.Platform.Messaging;
using Frappe.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Wolverine;
using Wolverine.Nats;
using Wolverine.Tracking;
using AuditConsumer = Frappe.Platform.IntegrationTests.Messaging.Audit.Application.TicketOpenedConsumer;
using BillingConsumer = Frappe.Platform.IntegrationTests.Messaging.Billing.Application.TicketOpenedConsumer;

namespace Frappe.Platform.IntegrationTests.Messaging;

// Every integration event goes outbox -> JetStream -> inbox. Each test takes a NATS scope of its own.
public sealed class NatsTransportTests(NatsFixture fixture) : IClassFixture<NatsFixture>
{
    private const string Invoices = "SELECT count(*) FROM billing.invoices WHERE ticket_id = $1";
    private const string AuditEntries = "SELECT count(*) FROM audit.entries WHERE ticket_id = $1";
    private const string ForTheId = "position(convert_to($1::text, 'UTF8') in body) > 0";

    private readonly Guid _tenant = Guid.CreateVersion7();
    private readonly Guid _ticket = Guid.CreateVersion7();
    private readonly NatsScope _scope = NatsScope.On(fixture.Server);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Consumer => NatsTopology.Consumer(_scope.Root, typeof(TicketOpened));

    [Fact]
    public async Task An_event_lands_on_its_subject_with_the_envelope_id_as_message_id_and_the_tenant()
    {
        await using IAlbaHost host = await fixture.StartHostAsync(_scope, Environments.Development);

        ITrackedSession session = await host.TrackAsync(_tenant, OpenTicket("ok"));

        NatsJSMsg<byte[]> stored = (
            await JetStreamReads.MessagesAboutAsync(_scope, _ticket)
        ).ShouldHaveSingleItem();
        stored.Subject.ShouldBe($"{_scope.Root}.kitchen.ticket.opened.v1");
        stored
            .Headers!["Nats-Msg-Id"]
            .ToString()
            .ShouldBe(
                session
                    .Sent.Envelopes()
                    .Single(static envelope =>
                        envelope.Message is TicketOpened
                        && string.Equals(
                            envelope.Destination!.Scheme,
                            "nats",
                            StringComparison.Ordinal
                        )
                    )
                    .Id.ToString()
            );
        stored.Headers["tenant-id"].ToString().ShouldBe(_tenant.ToString());
    }

    [Fact]
    public async Task A_handler_that_throws_after_publishing_puts_nothing_on_the_stream()
    {
        await using IAlbaHost host = await fixture.StartHostAsync(_scope, Environments.Development);

        await host.TrackAsync(
            _tenant,
            async services =>
                await Should.ThrowAsync<InvalidOperationException>(() =>
                    OpenTicket("bug")(services)
                ),
            assertExceptions: false
        );

        (await JetStreamReads.MessagesAboutAsync(_scope, _ticket)).ShouldBeEmpty();
    }

    // The api role publishes only; the worker consumes, and the api has no consumer to compete with it.
    [Fact]
    public async Task The_api_publishes_and_only_the_worker_handles_the_event()
    {
        await fixture.ProvisionAsync(_scope);
        await using IAlbaHost api = await fixture.StartHostAsync(
            _scope,
            Environments.Production,
            "api"
        );
        await using IAlbaHost worker = await fixture.StartHostAsync(
            _scope,
            Environments.Production,
            "worker"
        );

        ITrackedSession session = await api.TrackAsync(
            _tenant,
            OpenTicket("ok"),
            alsoTrack: worker
        );

        (await fixture.CountAsync(Invoices, _ticket)).ShouldBe(1);
        Guid workerNode = worker.GetRuntime().Options.UniqueNodeId;
        session
            .AllRecordsInOrder()
            .Where(static record =>
                record.MessageEventType == MessageEventType.ExecutionFinished
                && record.Message is TicketOpened
            )
            .Select(static record => record.UniqueNodeId)
            .Distinct()
            .ShouldBe([workerNode]);
        NatsListeners(api).ShouldBeEmpty();
        api.GetRuntime().Options.Durability.LeadershipEligible.ShouldBeFalse();
    }

    // Workers share one durable consumer per event, so each event reaches one of them once.
    [Fact]
    public async Task Competing_workers_handle_each_event_once()
    {
        Guid[] tickets = [.. Enumerable.Range(0, 10).Select(static _ => Guid.CreateVersion7())];
        await fixture.ProvisionAsync(_scope);
        await using IAlbaHost api = await fixture.StartHostAsync(
            _scope,
            Environments.Production,
            "api"
        );
        await using IAlbaHost first = await fixture.StartHostAsync(
            _scope,
            Environments.Production,
            "worker"
        );
        await using IAlbaHost second = await fixture.StartHostAsync(
            _scope,
            Environments.Production,
            "worker"
        );

        await api.TrackAsync(
            _tenant,
            async services =>
            {
                foreach (Guid ticket in tickets)
                {
                    await services
                        .GetRequiredService<IDispatcher>()
                        .InvokeAsync(new OpenTicket(ticket, "ok"), Cancellation);
                }
            },
            alsoTrack: [first, second]
        );

        tickets
            .Select(static ticket => Runs.Of(ticket, BillingConsumer.Name))
            .ShouldAllBe(static runs => runs == 1);
        (await JetStreamReads.ConsumerAsync(_scope, Consumer)).NumAckPending.ShouldBe(0);
    }

    // A backfill replays the stream to every module that subscribes; past the inbox window only the
    // consumers' own idempotency keeps the effects single.
    [Fact]
    public async Task A_replay_from_the_stream_reaches_every_module_and_idempotent_consumers_absorb_it()
    {
        await using (
            IAlbaHost first = await fixture.StartHostAsync(_scope, Environments.Development)
        )
        {
            await first.TrackAsync(_tenant, OpenTicket("ok"));
        }

        // The inbox window has passed: its handled rows are gone.
        await fixture.ExecuteAsync(
            $"DELETE FROM wolverine.wolverine_incoming_envelopes WHERE {ForTheId}",
            _ticket
        );
        await JetStreamReads.ReplayFromTheStartAsync(_scope, Consumer);
        await using IAlbaHost replaying = await fixture.StartHostAsync(
            _scope,
            Environments.Development
        );
        await Runs.WaitForAsync(_ticket, BillingConsumer.Name, runs: 2);
        await Runs.WaitForAsync(_ticket, AuditConsumer.Name, runs: 2);

        (await fixture.CountCommittedAsync("billing.invoices", Invoices, _ticket)).ShouldBe(1);
        (await fixture.CountCommittedAsync("audit.entries", AuditEntries, _ticket)).ShouldBe(2);
    }

    [Fact]
    public async Task A_consumer_bug_dead_letters_the_event_and_nats_never_redelivers_it()
    {
        BillingConsumer.Poisoned[_ticket] = true;
        await using IAlbaHost host = await fixture.StartHostAsync(_scope, Environments.Development);

        await host.TrackAsync(_tenant, OpenTicket("ok"), assertExceptions: false);

        Runs.Of(_ticket, BillingConsumer.Name).ShouldBe(1);
        (
            await fixture.ScalarAsync<string>(
                $"SELECT exception_type FROM wolverine.wolverine_dead_letters WHERE {ForTheId}",
                _ticket
            )
        ).ShouldBe(typeof(InvalidOperationException).FullName);
        ConsumerInfo consumer = await JetStreamReads.ConsumerAsync(_scope, Consumer);
        consumer.NumRedelivered.ShouldBe(0);
        consumer.NumAckPending.ShouldBe(0);
    }

    // With one subscribing module Wolverine runs the handler on the NATS listener itself, not on a local queue.
    [Fact]
    public async Task A_single_subscriber_runs_on_the_listener_inside_the_event_tenant()
    {
        await using IAlbaHost host = await fixture.StartHostAsync(
            _scope,
            Environments.Development,
            modules: [new KitchenModule(), new BillingModule()]
        );

        ITrackedSession session = await host.TrackAsync(_tenant, OpenTicket("ok"));

        session.Executed.SingleEnvelope<TicketOpened>().Destination!.Scheme.ShouldBe("nats");
        (
            await fixture.ScalarAsync<Guid>(
                "SELECT business_id FROM billing.invoices WHERE ticket_id = $1",
                _ticket
            )
        ).ShouldBe(_tenant);
    }

    [Fact]
    public async Task A_deployed_worker_does_not_start_when_the_stream_is_missing()
    {
        Exception error = await Should.ThrowAsync<Exception>(() => StartDeployedWorkerAsync());

        error.ToString().ShouldContain($"stream '{_scope.Stream}' does not exist");
    }

    [Fact]
    public async Task A_deployed_worker_does_not_start_when_the_stream_differs_from_its_declaration()
    {
        await fixture.ProvisionAsync(_scope);
        await JetStreamReads.UpdateStreamAsync(
            _scope,
            static config => config with { MaxAge = TimeSpan.FromDays(1) }
        );

        Exception error = await Should.ThrowAsync<Exception>(() => StartDeployedWorkerAsync());

        error.ToString().ShouldContain("MaxAge");
    }

    [Fact]
    public async Task A_deployed_worker_does_not_start_when_a_consumer_differs_from_its_declaration()
    {
        await fixture.ProvisionAsync(_scope);
        await JetStreamReads.UpdateConsumerAsync(
            _scope,
            Consumer,
            static config => config.MaxDeliver = 10
        );

        Exception error = await Should.ThrowAsync<Exception>(() => StartDeployedWorkerAsync());

        error.ToString().ShouldContain("MaxDeliver");
    }

    // Wolverine cannot declare max_msg_size, so the platform checks it next to Verify.
    [Fact]
    public async Task A_deployed_worker_does_not_start_when_the_stream_takes_messages_over_one_megabyte()
    {
        await fixture.ProvisionAsync(_scope);
        await JetStreamReads.UpdateStreamAsync(
            _scope,
            static config => config with { MaxMsgSize = 2 * 1024 * 1024 }
        );

        InvalidOperationException error = await Should.ThrowAsync<InvalidOperationException>(() =>
            StartDeployedWorkerAsync()
        );

        error.Message.ShouldContain($"{NatsTopology.MaxMessageSize}");
        error.Message.ShouldContain("eng/nats/provision.sh");
    }

    // A core NATS or buffered endpoint would lose events on a crash, so the host refuses to start with one.
    [Fact]
    public async Task A_nats_endpoint_that_is_not_durable_jetstream_fails_the_boot()
    {
        InvalidOperationException error = await Should.ThrowAsync<InvalidOperationException>(() =>
            fixture.StartHostAsync(
                _scope,
                Environments.Development,
                configure: static builder =>
                    builder.Services.ConfigureWolverine(static options =>
                        options.PublishMessage<SweepTickets>().ToNatsSubject("frappe.core.subject")
                    )
            )
        );

        error.Message.ShouldContain("durable JetStream");
        error.Message.ShouldContain("frappe.core.subject");
    }

    // Credentials are content in configuration; a failed start must not print them.
    [Fact]
    public async Task A_failed_start_prints_neither_the_jwt_nor_the_seed()
    {
        const string Jwt =
            "eyJ0eXAiOiJKV1QiLCJhbGciOiJlZDI1NTE5LW5rZXkifQ.secret-test-jwt-payload.signature";
        const string Seed = "SUAMK2FG4MI6UE3ACF3FK3OIQBCEIEZV7NSWFFEW63UXMRLFM2XLAXK4GY";
        LogCapture logs = new();

        Exception error = await Should.ThrowAsync<Exception>(() =>
            fixture.StartHostAsync(
                new NatsScope("nats://127.0.0.1:1", _scope.Root),
                Environments.Development,
                "worker",
                configure: builder =>
                {
                    builder.Configuration["Nats:Creds"] =
                        $"-----BEGIN NATS USER JWT-----\n{Jwt}\n------END NATS USER JWT------\n\n"
                        + $"-----BEGIN USER NKEY SEED-----\n{Seed}\n------END USER NKEY SEED------\n";
                    builder.Logging.AddProvider(logs);
                    builder.Services.ConfigureWolverine(static options =>
                        options.BrokerInitializationTimeout = TimeSpan.Zero
                    );
                }
            )
        );

        string printed =
            $"{error}\n{string.Join('\n', logs.Records.Select(static record => record.Text))}";
        printed.ShouldNotContain(Jwt);
        printed.ShouldNotContain(Seed);
        printed.ShouldContain("127.0.0.1:1");
    }

    private static Uri[] NatsListeners(IAlbaHost host) =>
        [
            .. host.GetRuntime()
                .Endpoints.ActiveListeners()
                .Select(static listener => listener.Uri)
                .Where(static uri => string.Equals(uri.Scheme, "nats", StringComparison.Ordinal)),
        ];

    private Func<IServiceProvider, Task> OpenTicket(string note) =>
        async services =>
        {
            ErrorOr<Guid> opened = await services
                .GetRequiredService<IDispatcher>()
                .InvokeAsync(new OpenTicket(_ticket, note), Cancellation);
            opened.IsError.ShouldBeFalse();
        };

    private async Task StartDeployedWorkerAsync()
    {
        await using IAlbaHost _ = await fixture.StartHostAsync(
            _scope,
            Environments.Production,
            "worker",
            configure: static builder =>
                builder.Services.ConfigureWolverine(static options =>
                    options.BrokerInitializationTimeout = TimeSpan.Zero
                )
        );
    }
}
