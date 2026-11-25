using Microsoft.Extensions.Options;

namespace Frappe.Notifications.Senders.Email;

/// <summary>Fails the host's start, naming the key, when the unsubscribe page or the API's address is missing or not http(s) (CFG-01).</summary>
public sealed class UnsubscribeLinkOptionsValidator : IValidateOptions<UnsubscribeLinkOptions>
{
    public ValidateOptionsResult Validate(string? name, UnsubscribeLinkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (!IsWebUrl(options.PageUrl))
        {
            failures.Add(
                $"Configuration '{UnsubscribeLinkOptions.Section}:PageUrl' must be set to an absolute http or https URL: "
                + "the page the visible unsubscribe link opens.");
        }

        if (!IsWebUrl(options.ApiBaseUrl) || !options.ApiBaseUrl!.AbsolutePath.EndsWith('/'))
        {
            failures.Add(
                $"Configuration '{UnsubscribeLinkOptions.Section}:ApiBaseUrl' must be set to an absolute http or https URL ending in a slash: "
                + "where mail clients reach this API.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsWebUrl(Uri? url) =>
        url is { IsAbsoluteUri: true } && url.Scheme is "http" or "https" && string.IsNullOrEmpty(url.UserInfo);
}
