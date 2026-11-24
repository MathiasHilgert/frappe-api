namespace Frappe.Identity.Application.Owners.UpdateMe;

/// <summary>
/// A member a merge patch (RFC 7396) names: absent from the patch means a <see langword="null"/> <c>Patched&lt;T&gt;</c>, and a member
/// sent as JSON <c>null</c> is a <see cref="Patched{T}"/> whose <see cref="Value"/> is <see langword="null"/>.
/// </summary>
/// <typeparam name="T">The member's type.</typeparam>
/// <param name="Value">The value the patch sets; <see langword="null"/> asks to clear the member.</param>
public sealed record Patched<T>(T? Value);
