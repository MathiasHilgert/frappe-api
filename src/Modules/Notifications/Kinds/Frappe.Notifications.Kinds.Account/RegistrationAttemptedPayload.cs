namespace Frappe.Notifications.Kinds.Account;

/// <summary>
/// What <see cref="RegistrationAttempted"/> needs: the address of the real owner and its language. The address is personal data:
/// <see cref="ToString"/> prints it as stars (S13, EVT-20).
/// </summary>
/// <param name="Email">The address the notice goes to; it is the subject of the fact (S3).</param>
/// <param name="Language">The recipient's language tag (<c>es-AR</c>); empty or unknown gets the default.</param>
public sealed record RegistrationAttemptedPayload(string Email, string? Language)
{
    public override string ToString() => "RegistrationAttemptedPayload { Email = ***, Language = " + Language + " }";
}
