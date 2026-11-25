using Frappe.Notifications.Infrastructure.Contacts;
using Microsoft.Extensions.Options;

namespace Frappe.Notifications.UnitTests.Deliveries;

/// <summary>Builds the real hasher over fixed test keys.</summary>
internal static class TestHasher
{
    public static HmacAddressHasher Create(short current = 1, params (short Id, string Key)[] extra)
    {
        var options = new AddressHashOptions { CurrentKeyId = current };
        options.Keys[1] = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray());
        foreach (var (id, key) in extra)
        {
            options.Keys[id] = key;
        }

        return new HmacAddressHasher(Options.Create(options));
    }
}
