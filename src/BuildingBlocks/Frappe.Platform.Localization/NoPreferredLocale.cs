using Frappe.SharedKernel.Localization;

namespace Frappe.Platform.Localization;

internal sealed class NoPreferredLocale : IPreferredLocaleSource
{
    public ValueTask<Locale?> GetPreferredLocaleAsync(CancellationToken cancellationToken) => ValueTask.FromResult<Locale?>(null);
}
