using Microsoft.Extensions.Hosting;

namespace Frappe.Platform.Http.Localization;

// Resolving the texts in its constructor makes the host start fail on a broken errors.<locale>.json resource.
internal sealed class ErrorTextsCheck(ErrorTexts texts) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        GC.KeepAlive(texts);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
