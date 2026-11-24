using Frappe.Notifications.Application.Kinds;

namespace Frappe.Notifications.Kinds.Account;

/// <summary>
/// What <see cref="EmailChangeCode"/> needs: the address that gets the code, its language, the person's name and the code itself. The code
/// is a secret and the address is personal data: <see cref="ToString"/> prints neither (S13, EVT-20).
/// </summary>
/// <param name="Email">The address the code goes to; it is the subject of the fact (S3).</param>
/// <param name="Language">The recipient's language tag (<c>es-AR</c>); empty or unknown gets the default.</param>
/// <param name="Name">What to call the person: at most 60 characters and nothing that looks like a link.</param>
/// <param name="Code">The verification code, as text.</param>
public sealed record EmailChangeCodePayload(string Email, string? Language, string Name, string Code) : IPayloadRules
{
    /// <inheritdoc />
    public IReadOnlyList<string> InvalidFields() => PayloadText.IsPersonName(Name) ? [] : ["name"];

    public override string ToString() => "EmailChangeCodePayload { Email = ***, Language = " + Language + ", Name = ***, Code = *** }";
}
