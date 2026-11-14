using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using NodaTime;

namespace Frappe.Geo.Countries.Argentina.Banking;

/// <summary>
/// The table of the BCRA's banking holidays page: one <c>&lt;table&gt;</c> headed Fecha / Motivo, one row per day, dates
/// written <c>02 abr 2026</c> with Spanish month abbreviations. Anything else on the page is ignored; a page without
/// exactly that table, or with a row it cannot read, is a <see cref="FormatException"/>.
/// </summary>
internal static partial class BcraHolidayTable
{
    private static readonly Dictionary<string, int> Months = new(StringComparer.Ordinal)
    {
        ["ene"] = 1,
        ["feb"] = 2,
        ["mar"] = 3,
        ["abr"] = 4,
        ["may"] = 5,
        ["jun"] = 6,
        ["jul"] = 7,
        ["ago"] = 8,
        ["sep"] = 9,
        ["set"] = 9,
        ["oct"] = 10,
        ["nov"] = 11,
        ["dic"] = 12,
    };

    public static IReadOnlyList<(LocalDate Date, string Reason)> Read(string html)
    {
        var tables = Table().Matches(html);
        if (tables.Count != 1)
        {
            throw new FormatException($"Expected one table of banking holidays, found {tables.Count}.");
        }

        var table = tables[0].Groups["body"].Value;
        if (!Header().IsMatch(table))
        {
            throw new FormatException("The table is not headed Fecha / Motivo.");
        }

        var rows = Row().Matches(table);
        if (rows.Count == 0 || rows.Count != RowStart().Count(table) - 1)
        {
            throw new FormatException($"The table has {RowStart().Count(table) - 1} rows, {rows.Count} of them readable.");
        }

        return [.. rows.Select(row => (DateOf(Text(row.Groups["date"].Value)), Text(row.Groups["reason"].Value)))];
    }

    /// <summary>
    /// The Comunicaciones the page says its list follows ("Listado correspondiente a 2026, según lo establecido en la
    /// Comunicación C 101352 ... y las comunicaciones C 102390 y A 8487"), in the order it names them, as their code
    /// (<c>C 101352</c>) and the link to their PDF as written. None when the page does not say: the list is still read.
    /// </summary>
    public static IReadOnlyList<(string Code, string Link)> Comunicaciones(string html)
    {
        var paragraph = ListBasis().Match(html);
        return paragraph.Success
            ? [.. ComunicacionLink().Matches(paragraph.Groups["body"].Value).Select(link => ($"{link.Groups["series"].Value} {link.Groups["number"].Value}", WebUtility.HtmlDecode(link.Groups["href"].Value)))]
            : [];
    }

    private static LocalDate DateOf(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || !Months.TryGetValue(parts[1].ToLowerInvariant(), out var month)
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var day)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || day < 1 || day > CalendarSystem.Iso.GetDaysInMonth(year, month))
        {
            throw new FormatException($"Unreadable date '{text}'.");
        }

        return new LocalDate(year, month, day);
    }

    /// <summary>A cell's text: tags dropped, entities decoded, white space collapsed.</summary>
    private static string Text(string cell) => Spaces().Replace(WebUtility.HtmlDecode(Tag().Replace(cell, " ")), " ").Trim();

    [GeneratedRegex(@"<table[^>]*>(?<body>.*?)</table>", RegexOptions.Singleline | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Table();

    [GeneratedRegex(@"<tr>\s*<th>\s*Fecha\s*</th>\s*<th>\s*Motivo\s*</th>\s*</tr>", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Header();

    [GeneratedRegex(@"<tr>\s*<td>(?<date>.*?)</td>\s*<td>(?<reason>.*?)</td>\s*</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Row();

    [GeneratedRegex(@"<tr[\s>]", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RowStart();

    [GeneratedRegex(@"<p>(?<body>(?:(?!</p>).)*?Listado correspondiente(?:(?!</p>).)*)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ListBasis();

    [GeneratedRegex(@"<a\s[^>]*href=""(?<href>[^""]*/comytexord/(?<series>[A-Z])(?<number>\d+)\.pdf)""", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ComunicacionLink();

    [GeneratedRegex("<[^>]*>", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Spaces();
}
