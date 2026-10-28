using System.ComponentModel.DataAnnotations;

namespace Frappe.Platform.Messaging;

// Credentials travel in Nats:Creds, never in the URL, so they reach no log or diagnostic that prints it. The
// message is fixed because the value may hold a password.
[AttributeUsage(AttributeTargets.Property)]
internal sealed class NatsUrlAttribute : ValidationAttribute
{
    private const string Invalid =
        "Nats:Url must be an absolute nats:// or tls:// URL without credentials; put them in Nats:Creds.";

    protected override ValidationResult? IsValid(
        object? value,
        ValidationContext validationContext
    ) =>
        value is not string url
        || string.IsNullOrWhiteSpace(url)
        || (
            Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            && uri.Scheme is "nats" or "tls"
            && uri.UserInfo.Length == 0
        )
            ? ValidationResult.Success
            : new ValidationResult(Invalid, [validationContext.MemberName ?? ""]);
}
