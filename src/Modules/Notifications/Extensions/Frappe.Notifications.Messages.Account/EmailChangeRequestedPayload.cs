using Frappe.Notifications.Application.Messages;

namespace Frappe.Notifications.Messages.Account;

/// <summary>
/// What <see cref="EmailChangeRequested"/> needs: the address in force (the one that gets the notice), its language, the person's name and the
/// requested address with its local part masked. Both addresses and the name are personal data: <see cref="ToString"/> prints none (S13, EVT-20).
/// </summary>
/// <param name="Email">The address in force, the one the notice goes to; it is the subject of the fact (S3).</param>
/// <param name="Language">The recipient's language tag (<c>es-AR</c>); empty or unknown gets the default.</param>
/// <param name="Name">What to call the person: at most 60 characters and nothing that looks like a link.</param>
/// <param name="NewEmailMasked">The requested address as <c>n***@domain</c>: it has an <c>@</c>, no spaces and no <c>://</c>.</param>
public sealed record EmailChangeRequestedPayload(string Email, string? Language, string Name, string NewEmailMasked) : IPayloadRules
{
    private const int MaxMaskedLength = 254;

    /// <inheritdoc />
    public IReadOnlyList<string> InvalidFields()
    {
        List<string> invalid = [];
        if (!PayloadText.IsPersonName(Name))
        {
            invalid.Add("name");
        }

        if (!IsMasked(NewEmailMasked))
        {
            invalid.Add("new_email_masked");
        }

        return invalid;
    }

    public override string ToString() =>
        "EmailChangeRequestedPayload { Email = ***, Language = " + Language + ", Name = ***, NewEmailMasked = *** }";

    private static bool IsMasked(string? value) =>
        value is { Length: > 0 and <= MaxMaskedLength }
        && value.Contains('@', StringComparison.Ordinal)
        && !value.Any(char.IsWhiteSpace)
        && !value.Contains("://", StringComparison.Ordinal);
}
