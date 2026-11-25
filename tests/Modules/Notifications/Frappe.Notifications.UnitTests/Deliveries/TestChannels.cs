using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.UnitTests.Deliveries;

/// <summary>The two channel names of the tests.</summary>
internal static class TestChannels
{
    public static readonly ChannelName Mail = ChannelName.Create("mail").Value;

    public static readonly ChannelName Text = ChannelName.Create("text").Value;
}
