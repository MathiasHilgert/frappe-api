using System.Globalization;
using System.Net;
using System.Text;
using Frappe.Notification.Application;
using Frappe.Notification.Domain;
using Frappe.Platform.Localization;
using Frappe.SharedKernel.Localization;

namespace Frappe.Notification.Infrastructure.Mails;

/// <summary>
/// Code mails from the message catalogue (<c>Mails/i18n</c>) in the black-and-white layout: the logo up front, the
/// code as six boxes, system fonts and no links, so nobody is taught to click links in mail.
/// </summary>
internal sealed class CatalogOwnerCodeMails(IMessageCatalog catalog, SupportedLocales supported) : IOwnerCodeMails
{
    private const string Prefix = "notification.mail.";
    private const string LogoId = "frappe-logo";
    private const string DarkLogoId = "frappe-logo-dark";

    private static readonly IReadOnlyList<InlineImage> Logos =
    [
        new(LogoId, "frappe.png", "image/png", Resource("logo-light.png")),
        new(DarkLogoId, "frappe-dark.png", "image/png", Resource("logo-dark.png")),
    ];

    public Mail Compose(OwnerCodeMailKind kind, string to, string name, string code, string? locale)
    {
        var language = Resolve(locale);
        var prefix = Prefix + kind switch
        {
            OwnerCodeMailKind.Verification => "verification.",
            OwnerCodeMailKind.PasswordReset => "password_reset.",
            OwnerCodeMailKind.EmailChange => "email_change.",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        var args = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code, ["name"] = name };
        string Text(string key) => catalog.Format(language, key, args);

        var title = Text(prefix + "title");
        var body = Text(prefix + "body");
        var footer = Text(prefix + "footer");
        var expiry = Text(Prefix + "expiry");
        var singleUse = Text(Prefix + "single_use");

        var text = $"{title}\n\n{body}\n\n{Text(Prefix + "code")}: {code}\n\n{expiry} · {singleUse}\n\n{footer}\n";
        var html = Layout(language, title, body, code, expiry, singleUse, footer);
        return new Mail(to, Text(prefix + "subject"), text, html, Logos);
    }

    // A locale the platform cannot speak still gets its mail, in the default language.
    private Locale Resolve(string? tag) =>
        Locale.Create(tag) is { IsSuccess: true } parsed && supported.Lookup(parsed.Value) is { } match ? match : supported.Default;

    private static string Layout(Locale locale, string title, string body, string code, string expiry, string singleUse, string footer)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        var digits = new StringBuilder();
        foreach (var digit in code)
        {
            digits.Append(CultureInfo.InvariantCulture,
                $"<td class=\"digit\" width=\"16%\" align=\"center\" style=\"border:2px solid #111111;border-radius:6px;font-size:30px;font-weight:700;line-height:1;padding:10px 0;font-variant-numeric:tabular-nums\">{E(digit.ToString())}</td>");
            digits.Append("<td width=\"6\"></td>");
        }

        // Tables and inline styles: the only layout every mail client renders. Dark-mode clients that honour
        // prefers-color-scheme get the inverted pair and the white logo; the others keep black on white.
        return $$"""
            <!doctype html>
            <html lang="{{E(locale.Value)}}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta name="color-scheme" content="light dark">
            <meta name="supported-color-schemes" content="light dark">
            <title>{{E(title)}}</title>
            <style>
            .logo-dark{display:none;max-height:0;overflow:hidden}
            @media (prefers-color-scheme:dark){
            .page,.card{background:#141414!important;color:#f2f2f2!important}
            .muted{color:#c9c9c6!important}.digit{border-color:#f2f2f2!important}
            .logo-light{display:none!important;max-height:0!important}.logo-dark{display:block!important;max-height:none!important}
            }
            </style>
            </head>
            <body class="page" style="margin:0;padding:24px 12px;background:#efefed;color:#111111;font-family:-apple-system,'Helvetica Neue',Helvetica,Arial,sans-serif">
            <table role="presentation" class="card" width="100%" cellspacing="0" cellpadding="0" style="max-width:460px;margin:0 auto;background:#ffffff;color:#111111">
            <tr><td style="padding:36px 30px 28px">
            <img class="logo-light" src="cid:{{LogoId}}" width="84" height="84" alt="Frappé" style="display:block;border:0">
            <!--[if !mso]><!--><img class="logo-dark" src="cid:{{DarkLogoId}}" width="84" height="84" alt="Frappé" style="display:none;border:0"><!--<![endif]-->
            <h1 style="margin:22px 0 0;font-size:30px;line-height:1.08;font-weight:700;letter-spacing:-0.025em">{{E(title)}}</h1>
            <p class="muted" style="margin:22px 0 0;font-size:15px;line-height:1.5;color:#3a3a38">{{E(body)}}</p>
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="margin-top:22px"><tr>{{digits}}</tr></table>
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="margin-top:22px;border-top:1px solid #e2e2df"><tr>
            <td class="muted" style="padding-top:14px;font-size:13px;color:#6b6b68">{{E(expiry)}}</td>
            <td class="muted" align="right" style="padding-top:14px;font-size:13px;color:#6b6b68">{{E(singleUse)}}</td>
            </tr></table>
            <p class="muted" style="margin:22px 0 0;font-size:12px;line-height:1.5;color:#8a8a87">{{E(footer)}}</p>
            </td></tr>
            </table>
            </body>
            </html>
            """;
    }

    private static byte[] Resource(string file)
    {
        var assembly = typeof(CatalogOwnerCodeMails).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(".Mails." + file, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
