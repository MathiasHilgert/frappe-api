using Frappe.Identity.Domain.Credentials;
using Frappe.Identity.Domain.Owners;

namespace Frappe.Identity.Http.Owners;

/// <summary>What a person gives to register: the name, the address they will sign in with and a password.</summary>
/// <param name="Name">Given and family name; together at most <see cref="OwnerName.MaxMailedLength"/> characters and no link.</param>
/// <param name="Email">The address a code is mailed to.</param>
/// <param name="Password"><see cref="Password.MinLength"/> to <see cref="Password.MaxLength"/> characters, not in a public breach list.</param>
public sealed record RegisterOwnerRequest(OwnerNameRequest? Name, string? Email, string? Password)
{
    /// <summary>Masked: the request holds personal data and a secret (DOM-06).</summary>
    public override string ToString() => "RegisterOwnerRequest { the content is hidden }";
}
