namespace Frappe.Identity.Http.Owners;

/// <summary>The address an owner asks a reset code for.</summary>
/// <param name="Email">The address the owner registered with.</param>
public sealed record RequestPasswordResetRequest(string? Email)
{
    /// <summary>Masked: the request holds an address (DOM-06).</summary>
    public override string ToString() => "RequestPasswordResetRequest { the content is hidden }";
}
