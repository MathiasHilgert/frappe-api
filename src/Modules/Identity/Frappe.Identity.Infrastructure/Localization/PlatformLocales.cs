using Frappe.Identity.Application.Account;
using Frappe.Platform.Localization;
using Frappe.SharedKernel.Localization;

namespace Frappe.Identity.Infrastructure.Localization;

/// <summary>The platform's configured <see cref="SupportedLocales"/>, as Identity's port.</summary>
internal sealed class PlatformLocales(SupportedLocales supported) : IPlatformLocales
{
    public Locale? Lookup(Locale requested) => supported.Lookup(requested);
}
