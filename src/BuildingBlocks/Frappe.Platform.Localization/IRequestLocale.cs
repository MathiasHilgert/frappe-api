using Frappe.SharedKernel.Localization;

namespace Frappe.Platform.Localization;

/// <summary>The locale negotiated for the current request; also declared in <c>Content-Language</c>.</summary>
public interface IRequestLocale
{
    Locale Current { get; }
}
