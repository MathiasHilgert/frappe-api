using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Owners.UpdateMe;

/// <summary>
/// Changes the caller's name and language with a merge patch (S10): a member the patch leaves out stays as it was, the family name may be
/// cleared, the given name and the language may not. Every member is checked before anything changes. It holds personal data, so it prints
/// none (DOM-06).
/// </summary>
/// <param name="PrincipalId">The owner, from the authenticated request.</param>
/// <param name="Given">The new given name, when the patch has one.</param>
/// <param name="Family">The new family name (or its removal), when the patch has one.</param>
/// <param name="Locale">The new language as a BCP 47 tag, when the patch has one.</param>
/// <param name="IfMatch">
/// The versions the caller accepts (<c>If-Match</c>): none when the caller sent no precondition, <see cref="AnyVersion"/> for <c>*</c>, an
/// empty list when the header could not be read, which no version matches.
/// </param>
public sealed record UpdateMe(
    Guid PrincipalId, Patched<string>? Given, Patched<string>? Family, Patched<string>? Locale, IReadOnlyList<string>? IfMatch) : ICommand<Unit>
{
    /// <summary>The version that stands for <c>*</c>: any current version matches.</summary>
    public const string AnyVersion = "*";

    public override string ToString() => "UpdateMe { the content is hidden }";
}
