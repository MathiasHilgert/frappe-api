using System.Text.RegularExpressions;

namespace Frappe.Notifications.Domain.Deliveries;

/// <summary>A recipient named by kind and id; <c>ref:&lt;kind&gt;:&lt;id&gt;</c> is its key. A directory resolves its address.</summary>
public sealed partial record ReferenceRecipient : Recipient
{
    /// <summary>The longest id a recipient key can hold.</summary>
    public const int MaxIdLength = 100;

    public ReferenceRecipient(string kind, string id)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!KindShape().IsMatch(kind) || id.Length > MaxIdLength)
        {
            throw new ArgumentException("A reference is a lower snake case kind and an id of at most 100 characters.", nameof(kind));
        }

        Kind = kind;
        Id = id;
    }

    public string Kind { get; }

    public string Id { get; }

    [GeneratedRegex("^[a-z_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex KindShape();
}
