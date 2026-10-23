using Frappe.SharedKernel.Localization;
using Microsoft.Net.Http.Headers;

namespace Frappe.Platform.Localization;

/// <summary>
/// Picks the locale of a response. An <c>Accept-Language</c> the platform can serve wins; otherwise the principal's
/// stored preference; otherwise the default. A header naming only unsupported languages does not override a stored
/// preference: serving the owner's own choice beats serving the default.
/// </summary>
public sealed class LocaleNegotiator(SupportedLocales supported)
{
    public Locale Negotiate(string? acceptLanguage, Locale? preferred) =>
        FromHeader(acceptLanguage)
        ?? (preferred is null ? null : supported.Lookup(preferred))
        ?? supported.Default;

    /// <summary>Each tag in descending quality (ties keep header order) is looked up; the first hit wins.</summary>
    public Locale? FromHeader(string? acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage)
            || !StringWithQualityHeaderValue.TryParseList([acceptLanguage], out var values))
            return null;

        return values
            .Select((value, index) => (value, index))
            .Where(x => x.value.Quality is not <= 0)
            .OrderByDescending(x => x.value.Quality ?? 1)
            .ThenBy(x => x.index)
            .Select(x => Locale.Create(x.value.Value.Value))
            .Where(result => result.IsSuccess)
            .Select(result => supported.Lookup(result.Value))
            .FirstOrDefault(match => match is not null);
    }
}
