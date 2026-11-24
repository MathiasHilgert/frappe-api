namespace Frappe.Notifications.Producers.Identity;

/// <summary>Where the links of a mail point, bound from <c>Notifications:Links</c>. Only the Worker, which builds them, binds it.</summary>
public sealed class NotificationLinksOptions
{
    /// <summary>The section the options are bound from.</summary>
    public const string Section = "Notifications:Links";

    /// <summary>
    /// <c>Notifications:Links:ConsoleUrl</c>: the console's base address; the undo page of an email change is <c>undo-email-change</c> under it.
    /// No default: required in the Worker. Range an absolute https URL, in every environment.
    /// </summary>
    public Uri? ConsoleUrl { get; set; }
}
