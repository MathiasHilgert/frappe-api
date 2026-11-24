namespace Frappe.Notifications.Kinds.Account;

/// <summary>
/// What <see cref="AccountActivationCode"/> needs: the address that gets the code, its language, the person's name and the code itself.
/// The code is a secret
/// and the address is personal data: <see cref="ToString"/> prints neither (S13, EVT-20).
/// </summary>
/// <param name="Email">The address the code goes to; it is the subject of the fact (S3).</param>
/// <param name="Language">The recipient's language tag (<c>es-AR</c>); empty or unknown gets the default.</param>
/// <param name="Name">What to call the person.</param>
/// <param name="Code">The verification code, as text.</param>
public sealed record AccountActivationCodePayload(string Email, string? Language, string Name, string Code)
{
    public override string ToString() => "AccountActivationCodePayload { Email = ***, Language = " + Language + ", Name = ***, Code = *** }";
}
