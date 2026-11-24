namespace Frappe.Identity.Http.Owners;

/// <summary>The two parts of a name.</summary>
/// <param name="Given">The given name; required.</param>
/// <param name="Family">The family name; optional.</param>
public sealed record OwnerNameRequest(string? Given, string? Family)
{
    /// <summary>Masked: a name is personal data (DOM-06).</summary>
    public override string ToString() => "OwnerNameRequest { the content is hidden }";
}
