namespace Frappe.Identity.Http.Owners;

/// <summary>The code that was mailed to the address.</summary>
/// <param name="Code">The six digits.</param>
public sealed record VerifyOwnerRequest(string? Code)
{
    /// <summary>Masked: a code is a secret (DOM-06).</summary>
    public override string ToString() => "VerifyOwnerRequest { the content is hidden }";
}
