namespace Frappe.Identity.Http.Owners;

/// <summary>The address the owner wants to change to.</summary>
/// <param name="Email">The new address.</param>
public sealed record RequestEmailChangeRequest(string? Email)
{
    /// <summary>Masked: the request holds an address (DOM-06).</summary>
    public override string ToString() => "RequestEmailChangeRequest { the content is hidden }";
}
