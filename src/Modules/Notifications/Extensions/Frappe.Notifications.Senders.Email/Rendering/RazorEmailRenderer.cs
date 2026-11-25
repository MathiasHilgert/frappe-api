using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;

namespace Frappe.Notifications.Senders.Email.Rendering;

/// <summary>
/// Renders an <see cref="EmailDocument"/> to HTML with the Razor components of this folder (S7). A renderer lives for one render: the same
/// document gives the same bytes every time, because the provider dedupes retries by an idempotency key and expects the same message.
/// </summary>
public sealed class RazorEmailRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
{
    public async Task<string> RenderAsync(EmailDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        await using var renderer = new HtmlRenderer(services, loggerFactory);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(EmailLayout.Document)] = document });
            var output = await renderer.RenderComponentAsync<EmailLayout>(parameters);
            return output.ToHtmlString();
        });
        return "<!DOCTYPE html>\n" + html;
    }
}
