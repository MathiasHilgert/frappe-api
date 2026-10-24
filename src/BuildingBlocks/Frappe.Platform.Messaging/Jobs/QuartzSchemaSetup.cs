namespace Frappe.Platform.Messaging.Jobs;

/// <summary>
/// Runs before the scheduler starts. Development provisions the Quartz store, as Wolverine does for its own; every
/// other environment only checks it is there and refuses to start, pointing at <c>resources setup</c>, when it is not.
/// </summary>
internal sealed class QuartzSchemaSetup(QuartzSchema schema, bool provision) : Microsoft.Extensions.Hosting.IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        provision ? schema.Setup(cancellationToken) : schema.Check(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
