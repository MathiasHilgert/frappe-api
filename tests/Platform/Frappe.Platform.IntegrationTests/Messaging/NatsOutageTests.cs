using Alba;
using Frappe.Kernel.Messaging;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Application;
using Frappe.Platform.Messaging;
using Frappe.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        await using IAlbaHost host = await fixture.StartHostAsync(scope, Environments.Development);
        await fixture.Server.StopAsync();

        await InvokeAsync(host);
        (await fixture.CountAsync(Outbox, _ticket)).ShouldBe(1);
        await fixture.Server.StartAsync();
        await Runs.WaitForAsync(_ticket, BillingConsumer.Name);

        (await fixture.CountAsync(Outbox, _ticket)).ShouldBe(0);
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
