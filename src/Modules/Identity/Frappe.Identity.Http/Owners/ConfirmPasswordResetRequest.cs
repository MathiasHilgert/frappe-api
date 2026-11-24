namespace Frappe.Identity.Http.Owners;

/// <summary>The code that was mailed and the password the owner wants.</summary>
/// <param name="Code">The six digits.</param>
/// <param name="NewPassword">The new password.</param>
public sealed record ConfirmPasswordResetRequest(string? Code, string? NewPassword)
{
    /// <summary>Masked: the request holds a code and a secret (DOM-06).</summary>
    public override string ToString() => "ConfirmPasswordResetRequest { the content is hidden }";
}
