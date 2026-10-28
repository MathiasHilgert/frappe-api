using Frappe.Kernel.Messaging;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Application;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Contracts;
using Frappe.Platform.Messaging;
using Frappe.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wolverine.Tracking;
using BillingConsumer = Frappe.Platform.IntegrationTests.Messaging.Billing.Application.TicketOpenedConsumer;

namespace Frappe.Platform.IntegrationTests.Messaging;

[Collection(MessagingTestGroup.Name)]
public sealed class MessagingTests(MessagingFixture fixture)
{
    private const string Tickets = "SELECT count(*) FROM kitchen.tickets WHERE id = $1";
    private const string Invoices = "SELECT count(*) FROM billing.invoices WHERE ticket_id = $1";
    private const string AuditEntries = "SELECT count(*) FROM audit.entries WHERE ticket_id = $1";

    // The bus's tables hold every test's envelopes; one is this test's when its body names the id.
    private const string ForTheId = "position(convert_to($1::text, 'UTF8') in body) > 0";
    private const string DeadLetters =
        $"SELECT count(*) FROM wolverine.wolverine_dead_letters WHERE {ForTheId}";

    private readonly Guid _tenant = Guid.CreateVersion7();
    private readonly Guid _ticket = Guid.CreateVersion7();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_invoked_command_runs_in_an_eager_transaction_for_the_scope_tenant()
    {
        ErrorOr<Guid> result = default;

        await TrackAsync(async dispatcher =>
            result = await dispatcher.InvokeAsync(new OpenTicket(_ticket, "ok"), Cancellation)
        );

        result.Value.ShouldBe(_ticket);
        (
            await fixture.ScalarAsync<Guid>(
                "SELECT business_id FROM kitchen.tickets WHERE id = $1",
                _ticket
            )
        ).ShouldBe(_tenant);
        (
            await fixture.ScalarAsync<bool>(
                "SELECT in_transaction FROM kitchen.tickets WHERE id = $1",
                _ticket
            )
        ).ShouldBeTrue();
    }

    // One scope may send for several tenants in turn; a tenant must never stick to the scope's bus.
    [Fact]
    public async Task Each_send_carries_the_tenant_current_at_that_send()
    {
        var other = Guid.CreateVersion7();
        var invoked = Guid.CreateVersion7();
        var queued = Guid.CreateVersion7();

        await TrackAsync(async dispatcher =>
        {
            await dispatcher.EnqueueAsync(new QueueTicket(_ticket, "ok"), Cancellation);
            using (TenantScope.Begin(other, branchId: null, TenantScope.SystemPrincipalId))
            {
                await dispatcher.InvokeAsync(new OpenTicket(invoked, "ok"), Cancellation);
                await dispatcher.EnqueueAsync(new QueueTicket(queued, "ok"), Cancellation);
            }
        });

        (await BusinessOfAsync(_ticket)).ShouldBe(_tenant);
        (await BusinessOfAsync(invoked)).ShouldBe(other);
        (await BusinessOfAsync(queued)).ShouldBe(other);
    }

    [Fact]
    public async Task An_event_published_by_a_committed_handler_reaches_every_subscribing_module_with_the_tenant()
    {
        await TrackAsync(dispatcher =>
            dispatcher.InvokeAsync(new OpenTicket(_ticket, "ok"), Cancellation)
        );

        (await fixture.CountAsync(AuditEntries, _ticket)).ShouldBe(1);
        (
            await fixture.ScalarAsync<Guid>(
                "SELECT business_id FROM billing.invoices WHERE ticket_id = $1",
                _ticket
            )
        ).ShouldBe(_tenant);
    }

    [Fact]
    public async Task A_handler_that_throws_after_publishing_persists_and_emits_nothing()
    {
        await TrackAsync(
            async dispatcher =>
                await Should.ThrowAsync<InvalidOperationException>(() =>
                    dispatcher.InvokeAsync(new OpenTicket(_ticket, "bug"), Cancellation)
                ),
            assertExceptions: false
        );

        (await fixture.CountAsync(Tickets, _ticket)).ShouldBe(0);
        Runs.Of(_ticket, BillingConsumer.Name).ShouldBe(0);
        (await fixture.CountAsync(AuditEntries, _ticket)).ShouldBe(0);
        (
            await fixture.CountAsync(
                $"SELECT count(*) FROM wolverine.wolverine_outgoing_envelopes WHERE {ForTheId}",
                _ticket
            )
        ).ShouldBe(0);
    }

    [Fact]
    public async Task An_error_returned_to_an_invoke_comes_back_and_persists_nothing()
    {
        ErrorOr<Guid> result = default;

        await TrackAsync(async dispatcher =>
            result = await dispatcher.InvokeAsync(new OpenTicket(_ticket, "reject"), Cancellation)
        );

        result.FirstError.Code.ShouldBe("kitchen.ticket.rejected");
        (await fixture.CountAsync(Tickets, _ticket)).ShouldBe(0);
        Runs.Of(_ticket, BillingConsumer.Name).ShouldBe(0);
    }

    [Fact]
    public async Task An_enqueued_command_that_returns_an_error_is_logged_acknowledged_and_never_cascaded()
    {
        int logged = fixture.Logs.Records.Count;
        ITrackedSession session = await TrackAsync(dispatcher =>
            dispatcher.EnqueueAsync(new QueueTicket(_ticket, "reject"), Cancellation)
        );

        Runs.Of(_ticket).ShouldBe(1);
        (await fixture.CountAsync(Tickets, _ticket)).ShouldBe(0);
        Runs.Of(_ticket, BillingConsumer.Name).ShouldBe(0);
        session
            .AllRecordsInOrder()
            .Where(static record => record.Message is not null)
            .Select(static record => record.Message!.GetType())
            .Distinct()
            .ShouldBe([typeof(QueueTicket)]);
        // Only this test's records: the collection runs one test at a time and waits for its messages.
        fixture
            .Logs.Records.Skip(logged)
            .ShouldContain(record =>
                record.EventId.Id == 101
                && record.Level == LogLevel.Warning
                && Equals(record.State["ErrorCodes"], "kitchen.ticket.rejected")
            );
    }

    // ErrorOrOutcome tells an inline invoke from a queued message by this flag alone; if an upgrade changes
    // what sets it, an invoked error would be dropped or a queued one cascaded.
    [Fact]
    public async Task Only_an_inline_invoke_is_marked_as_one()
    {
        var queued = Guid.CreateVersion7();

        ITrackedSession session = await TrackAsync(async dispatcher =>
        {
            await dispatcher.InvokeAsync(new OpenTicket(_ticket, "ok"), Cancellation);
            await dispatcher.EnqueueAsync(new QueueTicket(queued, "ok"), Cancellation);
        });

        ErrorOrOutcome.IsInlineInvoke(session.Executed.SingleEnvelope<OpenTicket>()).ShouldBeTrue();
        ErrorOrOutcome
            .IsInlineInvoke(session.Executed.SingleEnvelope<QueueTicket>())
            .ShouldBeFalse();
    }

    [Fact]
    public async Task A_transient_failure_is_retried_and_then_succeeds()
    {
        await TrackAsync(
            dispatcher => dispatcher.EnqueueAsync(new QueueTicket(_ticket, "flaky"), Cancellation),
            assertExceptions: false
        );

        Runs.Of(_ticket).ShouldBe(2);
        (await fixture.CountAsync(Tickets, _ticket)).ShouldBe(1);
        (await fixture.CountAsync(DeadLetters, _ticket)).ShouldBe(0);
    }

    [Fact]
    public async Task A_transient_failure_wrapped_in_another_exception_is_retried_and_then_succeeds()
    {
        await TrackAsync(
            dispatcher =>
                dispatcher.EnqueueAsync(new QueueTicket(_ticket, "wrapped-flaky"), Cancellation),
            assertExceptions: false
        );

        Runs.Of(_ticket).ShouldBe(2);
        (await fixture.CountAsync(Tickets, _ticket)).ShouldBe(1);
        (await fixture.CountAsync(DeadLetters, _ticket)).ShouldBe(0);
    }

    [Fact]
    public async Task A_bug_goes_to_the_dead_letters_without_a_retry()
    {
        await TrackAsync(
            dispatcher => dispatcher.EnqueueAsync(new QueueTicket(_ticket, "bug"), Cancellation),
            assertExceptions: false
        );

        Runs.Of(_ticket).ShouldBe(1);
        (await fixture.CountAsync(Tickets, _ticket)).ShouldBe(0);
        (
            await fixture.ScalarAsync<string>(
                $"SELECT exception_type FROM wolverine.wolverine_dead_letters WHERE {ForTheId}",
                _ticket
            )
        ).ShouldBe(typeof(InvalidOperationException).FullName);
    }

    [Fact]
    public async Task A_message_without_a_tenant_fails_before_its_handler_runs()
    {
        await TrackAsync(
            dispatcher => dispatcher.EnqueueAsync(new QueueTicket(_ticket, "ok"), Cancellation),
            assertExceptions: false,
            withTenant: false
        );

        Runs.Of(_ticket).ShouldBe(0);
        (await fixture.CountAsync(Tickets, _ticket)).ShouldBe(0);
        (
            await fixture.CountAsync(
                $"SELECT count(*) FROM wolverine.wolverine_dead_letters WHERE message_type = 'kitchen.queue_ticket' AND exception_message LIKE '%has no tenant%TenantScope.Begin%[GlobalMessage]%' AND {ForTheId}",
                _ticket
            )
        ).ShouldBe(1);
    }

    [Fact]
    public async Task A_global_message_runs_without_a_tenant()
    {
        await TrackAsync(
            dispatcher => dispatcher.EnqueueAsync(new SweepTickets(_ticket), Cancellation),
            withTenant: false
        );

        Runs.Of(_ticket).ShouldBe(1);
    }

    [Fact]
    public async Task The_same_event_delivered_twice_is_handled_once_by_an_idempotent_consumer()
    {
        await TrackAsync(
            async (IServiceProvider services) =>
            {
                IEventPublisher events = services.GetRequiredService<IEventPublisher>();
                await events.PublishAsync(new TicketOpened(_ticket), Cancellation);
                await events.PublishAsync(new TicketOpened(_ticket), Cancellation);
            }
        );

        Runs.Of(_ticket, BillingConsumer.Name).ShouldBe(2);
        (await fixture.CountAsync(Invoices, _ticket)).ShouldBe(1);
        (await fixture.CountAsync(AuditEntries, _ticket)).ShouldBe(2);
    }

    private Task<Guid> BusinessOfAsync(Guid ticket) =>
        fixture.ScalarAsync<Guid>("SELECT business_id FROM kitchen.tickets WHERE id = $1", ticket);

    private Task<ITrackedSession> TrackAsync(
        Func<IDispatcher, Task> act,
        bool assertExceptions = true,
        bool withTenant = true
    ) =>
        TrackAsync(
            services => act(services.GetRequiredService<IDispatcher>()),
            assertExceptions,
            withTenant
        );

    private Task<ITrackedSession> TrackAsync(
        Func<IServiceProvider, Task> act,
        bool assertExceptions = true,
        bool withTenant = true
    ) => fixture.Host.TrackAsync(withTenant ? _tenant : null, act, assertExceptions);
}
