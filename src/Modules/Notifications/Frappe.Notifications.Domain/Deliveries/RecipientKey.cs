namespace Frappe.Notifications.Domain.Deliveries;

/// <summary>
/// The stable identity of a recipient in deliveries and preferences: <c>ref:&lt;kind&gt;:&lt;id&gt;</c> for a reference, or
/// <c>addr:&lt;key id&gt;:&lt;hex hash&gt;</c> for an address, which is never stored in the clear (S13).
/// </summary>
public sealed record RecipientKey
{
    private RecipientKey(string value) => Value = value;

    public string Value { get; }

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
}
