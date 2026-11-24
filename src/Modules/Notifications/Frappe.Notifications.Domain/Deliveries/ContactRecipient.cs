using Frappe.Localization;

namespace Frappe.Notifications.Domain.Deliveries;

/// <summary>An explicit address over one channel and a language. It is personal data: <see cref="ToString"/> masks it (DOM-06).</summary>
public sealed record ContactRecipient : Recipient
{
    /// <summary>The longest address accepted.</summary>
    public const int MaxAddressLength = 320;

    public ContactRecipient(ChannelName channel, string address, LanguageTag? language = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(address.Length, MaxAddressLength);
        Channel = channel;
        Address = address;
        Language = language;
    }

    public ChannelName Channel { get; }

    public string Address { get; }

    public LanguageTag? Language { get; }

    public override string ToString() => $"ContactRecipient {{ Channel = {Channel.Value}, Address = ***, Language = {Language?.Value} }}";
}
