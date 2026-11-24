using Frappe.Notifications.Infrastructure.Unsubscribe;
using Microsoft.Extensions.Options;

namespace Frappe.Notifications.UnitTests.Preferences;

/// <summary>Builds the real token signer over fixed test keys.</summary>
internal static class TestUnsubscribeTokens
{
    public static readonly string KeyOne = Convert.ToBase64String(Enumerable.Repeat((byte)3, 32).ToArray());

    public static readonly string KeyTwo = Convert.ToBase64String(Enumerable.Repeat((byte)4, 32).ToArray());

    public static HmacUnsubscribeTokens Create(short current = 1, string? key = null, params (short Id, string Key)[] retired)
    {
        var options = new UnsubscribeOptions { CurrentKeyId = current, SigningKey = key ?? KeyOne };
        foreach (var (id, retiredKey) in retired)
        {
            options.RetiredKeys[id] = retiredKey;
        }

        return new HmacUnsubscribeTokens(Options.Create(options));
    }
}
