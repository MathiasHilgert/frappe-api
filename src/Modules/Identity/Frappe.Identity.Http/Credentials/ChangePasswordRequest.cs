namespace Frappe.Identity.Http.Credentials;

/// <summary>What a signed-in caller gives to change their password.</summary>
/// <param name="CurrentPassword">The password the caller has now.</param>
/// <param name="NewPassword">The password the caller wants.</param>
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword)
{
    /// <summary>Masked: the request holds two secrets (DOM-06).</summary>
    public override string ToString() => "ChangePasswordRequest { the content is hidden }";
}
