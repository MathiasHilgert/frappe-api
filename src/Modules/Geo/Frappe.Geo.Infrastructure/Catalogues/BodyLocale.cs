using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Kernel;
using Microsoft.AspNetCore.Http;

namespace Frappe.Geo.Infrastructure.Catalogues;

// The locale of the names a response carries: asked with ?locale= (English when absent) and named in the response.
internal static class BodyLocale
{
    public static ErrorOr<Locale> Of(string? locale) =>
        locale is null ? Locale.English : Locale.Create(locale).WithPointer("/locale");

    // RFC 9110 Content-Language names the language the body is in: where the names resolved, not what was asked.
    public static void Served(HttpContext context, Locale locale) =>
        context.Response.Headers.ContentLanguage = locale.Value;
}
