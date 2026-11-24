namespace Frappe.Identity.Http.Owners;

/// <summary>What an owner gives to log in.</summary>
/// <param name="Email">The address the owner registered with.</param>
/// <param name="Password">The owner's password.</param>
public sealed record LogInRequest(string? Email, string? Password)
{
    /// <summary>Masked: the request holds an address and a secret (DOM-06).</summary>
    public override string ToString() => "LogInRequest { the content is hidden }";
}
