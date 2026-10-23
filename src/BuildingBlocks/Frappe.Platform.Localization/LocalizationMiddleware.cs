using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Frappe.Platform.Localization;

/// <summary>Negotiates the request locale and declares it on every response, errors included.</summary>
internal sealed class LocalizationMiddleware(RequestDelegate next, LocaleNegotiator negotiator)
{
    public async Task InvokeAsync(HttpContext context, RequestLocale requestLocale, IPreferredLocaleSource preferences)
    {
        // The preference is only read when the header cannot decide, so most requests never touch storage.
        var locale = negotiator.FromHeader(context.Request.Headers.AcceptLanguage.ToString());
        locale ??= negotiator.Negotiate(null, await preferences.GetPreferredLocaleAsync(context.RequestAborted));
        requestLocale.Current = locale;

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.ContentLanguage = locale.Value;
            if (!headers.Vary.Any(v => v is not null && v.Contains(HeaderNames.AcceptLanguage, StringComparison.OrdinalIgnoreCase)))
                headers.Append(HeaderNames.Vary, HeaderNames.AcceptLanguage);
            return Task.CompletedTask;
        });

        await next(context);
    }
}
