using System.Text.RegularExpressions;

namespace Frappe.Notifications.Domain.Deliveries;

/// <summary>A recipient named by kind and id; <c>ref:&lt;kind&gt;:&lt;id&gt;</c> is its key. A directory resolves its address.</summary>
public sealed partial record ReferenceRecipient : Recipient
{
    /// <summary>The longest id a recipient key can hold.</summary>
    public const int MaxIdLength = 100;

    /// <summary>The longest kind, so <c>ref:&lt;kind&gt;:&lt;id&gt;</c> (145 characters at most) fits the <c>recipient_key</c> columns.</summary>
    public const int MaxKindLength = 40;

    public ReferenceRecipient(string kind, string id)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!KindShape().IsMatch(kind) || id.Length > MaxIdLength || HasControl().IsMatch(id))
        {
            throw new ArgumentException(
                "A reference is a lower snake case kind of at most 40 characters and an id of at most 100 characters without control characters.",
                nameof(kind));
        }

        Kind = kind;
        Id = id;
    }

    public string Kind { get; }

    public string Id { get; }

    [GeneratedRegex("^[a-z_]{1,40}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex KindShape();

    [GeneratedRegex(@"\p{C}", RegexOptions.CultureInvariant)]
    private static partial Regex HasControl();
}
