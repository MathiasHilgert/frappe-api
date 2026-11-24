using Frappe.Identity.Domain.Codes;

namespace Frappe.Identity.Application.Codes;

/// <summary>A code just redeemed: what it proved, for the caller to act on (activate the owner, change the address).</summary>
/// <param name="Id">The id of the stored code.</param>
/// <param name="Purpose">What the code proved.</param>
/// <param name="Target">The normalized address the code was sent to.</param>
public sealed record RedeemedCode(Guid Id, CodePurpose Purpose, string Target);
