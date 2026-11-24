using Microsoft.Extensions.Options;

namespace Frappe.Notifications.Channels.Email;

/// <summary>
/// Fails the host's start, naming the key, when the chosen sender cannot work: Resend without its key, an unknown sender, a rate at or above
/// the provider's limit or a total budget that does not cover one attempt (DES-04, CFG-01, CFG-09). It is hand-written because what is
/// required depends on the sender chosen (CFG-03).
/// </summary>
public sealed class EmailOptionsValidator : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (!Enum.IsDefined(options.Sender))
        {
            failures.Add($"Configuration '{EmailOptions.Section}:Sender' must be one of {string.Join(", ", Enum.GetNames<EmailSenderKind>())}.");
        }

        if (string.IsNullOrWhiteSpace(options.From))
        {
            failures.Add($"Configuration '{EmailOptions.Section}:From' must be set.");
        }

        ValidateResend(options, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateResend(EmailOptions options, List<string> failures)
    {
        var resend = options.Resend;
        if (options.Sender == EmailSenderKind.Resend && string.IsNullOrWhiteSpace(resend.ApiKey))
        {
            failures.Add($"Configuration '{ResendOptions.Section}:ApiKey' must be set when '{EmailOptions.Section}:Sender' is Resend.");
        }

        if (resend.RequestsPerSecond is < 1 or >= ResendOptions.ProviderRequestsPerSecond)
        {
            failures.Add(
                $"Configuration '{ResendOptions.Section}:RequestsPerSecond' must be from 1 to {ResendOptions.ProviderRequestsPerSecond - 1}.");
        }

        ValidateBudgets(resend, failures);
    }

    private static void ValidateBudgets(ResendOptions resend, List<string> failures)
    {
        if (resend.AttemptTimeout <= TimeSpan.Zero)
        {
            failures.Add($"Configuration '{ResendOptions.Section}:AttemptTimeout' must be longer than zero.");
        }

        if (resend.TotalTimeout <= resend.AttemptTimeout)
        {
            failures.Add($"Configuration '{ResendOptions.Section}:TotalTimeout' must be longer than '{ResendOptions.Section}:AttemptTimeout'.");
        }

        if (resend.Retries is < 1 or > 10)
        {
            failures.Add($"Configuration '{ResendOptions.Section}:Retries' must be from 1 to 10.");
        }

        if (resend.RetryDelay < TimeSpan.Zero)
        {
            failures.Add($"Configuration '{ResendOptions.Section}:RetryDelay' must not be negative.");
        }

        if (!resend.BaseUrl.IsAbsoluteUri || resend.BaseUrl.Scheme != Uri.UriSchemeHttps || !resend.BaseUrl.AbsolutePath.EndsWith('/'))
        {
            failures.Add($"Configuration '{ResendOptions.Section}:BaseUrl' must be an absolute https URL ending in a slash.");
        }
    }
}
