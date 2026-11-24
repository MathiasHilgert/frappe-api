using System.Text;
using Frappe.Results;

namespace Frappe.Notifications.Channels.Email;

/// <summary>
/// An email address in its one stored form: trimmed, lower case, Unicode NFC. It is the form the keyed hash of the address takes as input
/// (S13), so the same person hashes to the same value however the address was typed. Personal data: <see cref="ToString"/> masks it.
/// </summary>
public sealed record EmailAddress
{
    private const int MaxLength = 320;

    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public static Result<EmailAddress> Create(string? raw)
    {
        var value = raw?.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormC) ?? string.Empty;
        return IsShaped(value) ? new EmailAddress(value) : Error.InvalidValue("notifications.email_address.invalid_format");
    }

    public override string ToString() => "EmailAddress { Value = *** }";

    private static bool IsShaped(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        return value.Length is > 0 and <= MaxLength
            && at > 0
            && at == value.LastIndexOf('@')
            && at < value.Length - 1
            && !value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character is ',' or ';' or '<' or '>');
    }
}
