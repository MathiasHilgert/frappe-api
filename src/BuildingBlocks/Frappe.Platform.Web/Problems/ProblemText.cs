using Frappe.Platform.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Platform.Web.Problems;

/// <summary>Catalogue lookups in the request locale; without the localization block registered, everything stays English.</summary>
internal readonly struct ProblemText(IMessageCatalog? catalog, IRequestLocale? locale)
{
    public static ProblemText For(HttpContext context) =>
        new(context.RequestServices.GetService<IMessageCatalog>(), context.RequestServices.GetService<IRequestLocale>());

    public string Translate(string key, string fallback) =>
        catalog is not null && locale is not null ? catalog.TryFormat(locale.Current, key) ?? fallback : fallback;
}
