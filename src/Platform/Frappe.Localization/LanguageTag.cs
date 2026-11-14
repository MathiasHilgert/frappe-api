using System.Text.RegularExpressions;
using Frappe.Results;

namespace Frappe.Localization;

/// <summary>
/// One BCP 47 language tag (language, optional script, optional region), normalized:
/// <c>es</c>, <c>es-AR</c>, <c>es-419</c>, <c>zh-Hant-TW</c>. Wildcards and lists are not tags.
/// </summary>
public sealed partial record LanguageTag
{
    /// <summary>
    /// The column width for a stored tag. The tags this type accepts are at most 12 characters
    /// (language-Script-REGION, such as <c>zh-Hant-TW</c> or <c>ast-Latn-419</c>); 35 is the minimum RFC 5646
    /// section 4.4.1 asks implementations to support, kept as headroom.
    /// </summary>
    public const int StorageLength = 35;

    private LanguageTag(string value) => Value = value;

    public string Value { get; }

    public static Result<LanguageTag> Create(string? raw)
    {
        var match = Shape().Match(raw?.Trim() ?? string.Empty);
        if (!match.Success)
        {
            return Error.InvalidValue("localization.language_tag.invalid_format", ("value", raw));
        }

        var language = match.Groups["language"].Value.ToLowerInvariant();
        var script = match.Groups["script"].Value;
        var region = match.Groups["region"].Value.ToUpperInvariant();

        var normalized = language
            + (script.Length > 0 ? "-" + char.ToUpperInvariant(script[0]) + script[1..].ToLowerInvariant() : string.Empty)
            + (region.Length > 0 ? "-" + region : string.Empty);

        return new LanguageTag(normalized);
    }

    /// <summary>
    /// RFC 4647 lookup order: the tag itself, then each shorter tag obtained by dropping the last subtag.
    /// It never leaves the requested language.
    /// </summary>
    public IEnumerable<LanguageTag> LookupChain()
    {
        var current = Value;
        while (true)
        {
            yield return new LanguageTag(current);
            var cut = current.LastIndexOf('-');
            if (cut < 0)
            {
                yield break;
            }

            current = current[..cut];
        }
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^(?<language>[A-Za-z]{2,3})(?:-(?<script>[A-Za-z]{4}))?(?:-(?<region>[A-Za-z]{2}|[0-9]{3}))?$")]
    private static partial Regex Shape();
}
