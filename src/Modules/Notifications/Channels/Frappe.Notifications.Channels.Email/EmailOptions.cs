namespace Frappe.Notifications.Channels.Email;

/// <summary>The email channel's settings, bound from <c>Notifications:Email</c> and validated when the host starts (CFG-01).</summary>
public sealed class EmailOptions
{
    /// <summary>The section the options are bound from.</summary>
    public const string Section = "Notifications:Email";

    /// <summary>
    /// <c>Notifications:Email:Sender</c>: which adapter sends. Default <c>Resend</c>, so a deploy that forgets its key fails at start
    /// instead of silently logging. Range <see cref="EmailSenderKind"/>. Development and tests set <c>Logging</c>.
    /// </summary>
    public EmailSenderKind Sender { get; set; } = EmailSenderKind.Resend;

    /// <summary>
    /// <c>Notifications:Email:From</c>: the address emails come from (an address, or <c>Name &lt;address&gt;</c>). Default
    /// <c>onboarding@resend.dev</c>, Resend's sandbox sender, which only reaches the account owner: set the verified domain's address
    /// before sending to anyone else. Range not blank.
    /// </summary>
    public string From { get; set; } = "onboarding@resend.dev";

    /// <summary>The Resend adapter's settings (<c>Notifications:Email:Resend</c>).</summary>
    public ResendOptions Resend { get; } = new();
}
