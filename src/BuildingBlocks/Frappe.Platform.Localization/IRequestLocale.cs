using Frappe.SharedKernel.Localization;

namespace Frappe.Platform.Localization;

/// <summary>The locale negotiated for the current request; also declared in <c>Content-Language</c>.</summary>
public interface IRequestLocale
{
    Locale Current { get; }

    /// <summary>
    /// Re-negotiates after the stored preference changed mid-request, so the response is declared in it;
    /// an explicit <c>Accept-Language</c> still wins.
    /// </summary>
    void AdoptPreferred(Locale preferred);
}
