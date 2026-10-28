using Frappe.TestUtilities;
using Microsoft.Extensions.Hosting;

namespace Frappe.Platform.IntegrationTests.Messaging;

// A publishing role starts without NATS; that tolerance must not let it start on a broken message store.
// It breaks the fixture's store, so it runs in the outage collection, alone.
[Collection(NatsOutageGroup.Name)]
public sealed class MessageStoreStartupTests(NatsOutageFixture fixture) : IAsyncDisposable
{
    [Theory]
    [InlineData("api")]
    [InlineData("scheduler")]
    public async Task A_deployed_publishing_role_does_not_start_when_the_message_store_has_drifted(
        string role
    )
    {
        await fixture.ExecuteAsync(
            "ALTER TABLE wolverine.wolverine_outgoing_envelopes RENAME TO wolverine_outgoing_envelopes_gone"
        );

        Exception error = await Should.ThrowAsync<Exception>(() =>
            fixture.StartHostAsync(NatsScope.On(fixture.Server), Environments.Production, role)
        );

        error.ToString().ShouldContain("wolverine_outgoing_envelopes");
    }

    public async ValueTask DisposeAsync() =>
        await fixture.ExecuteAsync(
            "ALTER TABLE IF EXISTS wolverine.wolverine_outgoing_envelopes_gone RENAME TO wolverine_outgoing_envelopes"
        );
}
