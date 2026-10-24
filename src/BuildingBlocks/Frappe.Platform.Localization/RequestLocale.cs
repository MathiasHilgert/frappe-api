using Frappe.SharedKernel.Localization;

namespace Frappe.Platform.Localization;

internal sealed class RequestLocale(SupportedLocales supported) : IRequestLocale
{
    public Locale Current { get; set; } = supported.Default;

    /// <summary>True when an explicit <c>Accept-Language</c> decided the locale.</summary>
    public bool FromHeader { get; set; }

    public void AdoptPreferred(Locale preferred)
    {
        if (!FromHeader)
            Current = preferred;
    }
}
