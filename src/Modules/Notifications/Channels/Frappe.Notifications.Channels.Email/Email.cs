namespace Frappe.Notifications.Channels.Email;

/// <summary>Where a notification kind starts composing its email: the builder it gets decides what it may add.</summary>
public static class Email
{
    /// <summary>A Security email: a builder with no <c>Action</c>, so a link does not compile.</summary>
    public static SecureEmail Secure(string subject) => new(subject);

    /// <summary>An email that may carry a link (Transactional, Marketing).</summary>
    public static StandardEmail Standard(string subject) => new(subject);
}
