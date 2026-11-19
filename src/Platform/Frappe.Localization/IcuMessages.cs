using System.Globalization;
using Jeffijoe.MessageFormat;

namespace Frappe.Localization;

/// <summary>
/// ICU MessageFormat for every text Frappé renders (the error messages, the mail texts): a pattern is formatted in the
/// culture of the language it is written in, so plurals and numbers follow that language's rules. One formatter, with
/// its pattern cache, serves the whole process.
/// </summary>
public static class IcuMessages
{
    private static readonly MessageFormatter Formatter = new(useCache: true);

    /// <summary>
    /// <paramref name="pattern"/> with <paramref name="arguments"/>, in the culture of <paramref name="language"/>
    /// (<see cref="CultureOf(LanguageTag)"/>). A malformed pattern or a missing argument throws.
    /// </summary>
    public static string Format(string pattern, IReadOnlyDictionary<string, object?> arguments, LanguageTag language)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(language);
        return Formatter.FormatMessage(pattern, arguments, CultureOf(language));
    }

    /// <summary>
    /// The culture numbers and plurals are formatted in. The hosts run with ICU (<c>InvariantGlobalization</c> is false in
    /// <c>Directory.Build.props</c>), so every language
    /// Frappé is written in exists. Should a host ever run without it, a culture that cannot be found formats in the
    /// invariant culture instead of failing: the texts stay in their language, only number formats change.
    /// </summary>
    public static CultureInfo CultureOf(LanguageTag language)
    {
        ArgumentNullException.ThrowIfNull(language);
        return CultureOf(language.Value, CultureInfo.GetCultureInfo);
    }

    /// <summary><see cref="CultureOf(LanguageTag)"/> with the lookup given, so a test can stand for a host without ICU.</summary>
    internal static CultureInfo CultureOf(string language, Func<string, CultureInfo> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        try
        {
            return lookup(language);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
