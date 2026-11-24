namespace Frappe.Notifications.Channels.Email.Rendering;

/// <summary>
/// The Frappé design tokens as the email needs them (S7): the light values are written inline on every element, so a client that drops the
/// <c>style</c> block still shows the light design, and this block swaps them for the dark ones under <c>prefers-color-scheme</c> and under
/// the <c>[data-ogsc]</c> / <c>[data-ogsb]</c> hooks Outlook.com sets. It is a constant, so the bytes never change between attempts.
/// </summary>
public static class EmailStyles
{
    private const string DarkRules =
        ".bg-ground{background:#151013 !important;}"
        + ".bg-surface{background:#201a1d !important;box-shadow:none !important;}"
        + ".ink{color:#f4eef0 !important;}"
        + ".muted{color:#b8aab1 !important;}"
        + ".rule{border-color:#3a3036 !important;}"
        + ".box{border-color:#f4eef0 !important;color:#f4eef0 !important;}"
        + ".btn{background:#f2c4d1 !important;}"
        + ".btn-text{color:#1f1418 !important;}"
        + ".logo-light{display:none !important;}"
        + ".logo-dark{display:block !important;max-height:none !important;}";

    private const string OutlookRules =
        "[data-ogsb] .bg-ground{background-color:#151013 !important;}"
        + "[data-ogsb] .bg-surface{background-color:#201a1d !important;}"
        + "[data-ogsb] .btn{background-color:#f2c4d1 !important;}"
        + "[data-ogsc] .ink{color:#f4eef0 !important;}"
        + "[data-ogsc] .muted{color:#b8aab1 !important;}"
        + "[data-ogsc] .rule{border-color:#3a3036 !important;}"
        + "[data-ogsc] .box{border-color:#f4eef0 !important;color:#f4eef0 !important;}"
        + "[data-ogsc] .btn-text{color:#1f1418 !important;}"
        + "[data-ogsc] .logo-light{display:none !important;}"
        + "[data-ogsc] .logo-dark{display:block !important;max-height:none !important;}";

    /// <summary>The whole <c>style</c> block.</summary>
    public const string Css =
        ":root{color-scheme:light dark;supported-color-schemes:light dark;}"
        + "@media (max-width:480px){.card{padding:24px 24px 0 24px !important;}}"
        + "@media (prefers-color-scheme:dark){" + DarkRules + "}"
        + OutlookRules;
}
