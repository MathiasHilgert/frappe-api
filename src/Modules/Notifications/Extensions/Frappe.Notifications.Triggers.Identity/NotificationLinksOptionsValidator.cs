using Microsoft.Extensions.Options;

namespace Frappe.Notifications.Triggers.Identity;

/// <summary>
/// Fails the host's start, naming the key, when the console address is missing, not absolute or not https (CFG-01).
/// </summary>
public sealed class NotificationLinksOptionsValidator : IValidateOptions<NotificationLinksOptions>
{
    public ValidateOptionsResult Validate(string? name, NotificationLinksOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var url = options.ConsoleUrl;
        var schemeOk = url is { IsAbsoluteUri: true }
            && url.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(url.UserInfo);
        return schemeOk
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Configuration '{NotificationLinksOptions.Section}:ConsoleUrl' must be set to an absolute https URL: "
                + "the console the mail links open.");
    }
}
