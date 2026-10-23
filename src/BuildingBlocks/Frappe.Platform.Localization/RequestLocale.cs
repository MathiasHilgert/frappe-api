using Frappe.SharedKernel.Localization;

namespace Frappe.Platform.Localization;

internal sealed class RequestLocale(SupportedLocales supported) : IRequestLocale
{
    public Locale Current { get; set; } = supported.Default;
}
