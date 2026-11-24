namespace Frappe.Identity.Http.Owners;

/// <summary>
/// The merge patch (RFC 7396) of the profile: a member left out stays as it is. It documents the body; the endpoint reads the request itself, to
/// tell a member that is absent from one that is <c>null</c>, and to refuse unknown and repeated members.
/// </summary>
/// <param name="Name">The name members to change.</param>
/// <param name="Locale">
/// A BCP 47 language tag Frappé is written in (<c>en</c>, <c>es</c>, <c>es-AR</c>, <c>es-MX</c>, <c>pt-BR</c>, or one that narrows to them).
/// </param>
public sealed record MeMergePatch(MeNamePatch? Name, string? Locale);
