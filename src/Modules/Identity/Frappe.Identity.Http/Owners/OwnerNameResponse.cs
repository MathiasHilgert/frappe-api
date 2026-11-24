namespace Frappe.Identity.Http.Owners;

/// <summary>The two parts of an owner's name, as they were typed.</summary>
/// <param name="Given">The given name.</param>
/// <param name="Family">The family name; null when the owner has none.</param>
public sealed record OwnerNameResponse(string Given, string? Family)
{
    /// <summary>Masked: a name is personal data (DOM-06).</summary>
    public override string ToString() => "OwnerNameResponse { the content is hidden }";
}
