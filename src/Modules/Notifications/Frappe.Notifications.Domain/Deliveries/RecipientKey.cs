using System.Text.RegularExpressions;
using Frappe.Results;

namespace Frappe.Notifications.Domain.Deliveries;

/// <summary>
/// The stable identity of a recipient in deliveries and preferences: <c>ref:&lt;kind&gt;:&lt;id&gt;</c> for a reference, or
/// <c>addr:&lt;key id&gt;:&lt;hex hash&gt;</c> for an address, which is never stored in the clear (S13).
/// </summary>
public sealed partial record RecipientKey
{
    /// <summary>The longest key, as the <c>recipient_key</c> columns hold it.</summary>
    public const int MaxLength = 150;

    private const string AddressPrefix = "addr:";

    private RecipientKey(string value) => Value = value;

    public string Value { get; }

    /// <summary>Whether the key names an address by its keyed hash, which changes when the hash key rotates, rather than a reference.</summary>
    public bool IsAddress => Value.StartsWith(AddressPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Reads a key back from its text, for a key a signed token carried. It accepts only what the stored spelling allows (the shape the tables
    /// check), so no other text becomes a key.
    /// </summary>
    public static Result<RecipientKey> Parse(string? text) =>
        text is { Length: > 0 and <= MaxLength } && Spelling().IsMatch(text)
            ? new RecipientKey(text)
            : Error.InvalidValue("notifications.recipient_key.invalid");

    public static RecipientKey Of(ReferenceRecipient reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return new RecipientKey($"ref:{reference.Kind}:{reference.Id}");
    }

    public static RecipientKey Of(AddressHash hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        return new RecipientKey($"addr:{hash.KeyId}:{hash.Hex}");
    }

    public override string ToString() => Value;

    [GeneratedRegex(
        @"^(ref:[a-z_]{1,40}:(?!\s+\z)[^\p{C}]{1,100}|addr:[0-9]+:[0-9a-f]{64})\z",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex Spelling();
}
