namespace Frappe.Identity.Http.Owners;

/// <summary>The code that was mailed to the new address.</summary>
/// <param name="Code">The six digits.</param>
public sealed record ConfirmEmailChangeRequest(string? Code)
{
    /// <summary>Masked: the request holds a code (DOM-06).</summary>
    public override string ToString() => "ConfirmEmailChangeRequest { the content is hidden }";
}
