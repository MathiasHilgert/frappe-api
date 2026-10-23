using Frappe.SharedKernel.Localization;

namespace Frappe.Platform.Localization;

/// <summary>
/// The stored locale preference of the current principal. A module that owns principals implements it and
/// registers it; until one does, nobody has a preference.
/// </summary>
public interface IPreferredLocaleSource
{
    /// <summary>The stored preference of the current principal.</summary>
    /// <returns>The preference, or <see langword="null"/> when anonymous or none is stored.</returns>
    ValueTask<Locale?> GetPreferredLocaleAsync(CancellationToken cancellationToken);
}
