namespace Frappe.Notifications.Application.Feedback;

/// <summary>
/// A webhook call as the provider's parser receives it: the headers (looked up case-insensitively) and the body exactly as it arrived, since
/// a signature covers the raw bytes and re-serializing would break it. It holds no ASP.NET type, so a parser needs no web stack.
/// </summary>
/// <param name="Headers">The request headers by name; an absent header is absent.</param>
/// <param name="Body">The raw body bytes. It holds addresses (S13): never logged.</param>
public sealed record ProviderWebhook(IReadOnlyDictionary<string, string> Headers, ReadOnlyMemory<byte> Body)
{
    /// <summary>The value of a header, or <see langword="null"/> when the call did not carry it.</summary>
    public string? Header(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var (key, value) in Headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    public override string ToString() => "ProviderWebhook { Body = *** }";
}
