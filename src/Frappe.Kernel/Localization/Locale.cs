namespace Frappe.Kernel.Localization;

/// <summary>
/// A BCP 47 language tag in canonical case, such as <c>es-AR</c> or <c>zh-Hant-TW</c>: language, optional script,
/// optional region and variants; extensions and private use are not supported.
/// </summary>
public sealed record Locale
{
    private const int MaxLength = 35;
    private const int ScriptLength = 4;
    private const int RegionLetters = 2;
    private const int RegionDigits = 3;
    private const int MinLetterVariantLength = 5;
    private const int MaxVariantLength = 8;
    private const int DigitVariantLength = 4;

    private Locale(string value, string language)
    {
        Value = value;
        Language = string.Equals(value, language, StringComparison.Ordinal)
            ? this
            : new Locale(language, language);
    }

    /// <summary>Gets the error returned for a value outside the supported BCP 47 subset.</summary>
    public static Error Invalid { get; } =
        Error.Validation(
            "kernel.locale.invalid",
            "The locale must be a BCP 47 language tag such as es-AR."
        );

    /// <summary>Gets English, the last step of every fallback chain.</summary>
    public static Locale English { get; } = new("en", "en");

    /// <summary>Gets the canonical tag.</summary>
    public string Value { get; }

    /// <summary>Gets the locale of the primary language subtag alone, such as <c>es</c> for <c>es-AR</c>.</summary>
    public Locale Language { get; }

    /// <summary>Builds a locale from untrusted input: trimmed, <c>_</c> read as <c>-</c>, each subtag in canonical case.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The locale, or <see cref="Invalid"/>.</returns>
    public static ErrorOr<Locale> Create(string? value)
    {
        string tag = value?.Trim().Replace('_', '-') ?? string.Empty;
        if (tag.Length is 0 or > MaxLength)
        {
            return Invalid;
        }

        string[] subtags = tag.Split('-');
        string language = subtags[0];
        if (language.Length is < 2 or > 3 || !language.All(char.IsAsciiLetter))
        {
            return Invalid;
        }

        subtags[0] = language.ToLowerInvariant();
        int index = 1;
        if (index < subtags.Length && IsScript(subtags[index]))
        {
            subtags[index] =
                char.ToUpperInvariant(subtags[index][0]) + subtags[index][1..].ToLowerInvariant();
            index++;
        }

        if (index < subtags.Length && IsRegion(subtags[index]))
        {
            subtags[index] = subtags[index].ToUpperInvariant();
            index++;
        }

        for (; index < subtags.Length; index++)
        {
            if (!IsVariant(subtags[index]))
            {
                return Invalid;
            }

            subtags[index] = subtags[index].ToLowerInvariant();
        }

        return new Locale(string.Join('-', subtags), subtags[0]);
    }

    /// <summary>Gets the locales to try in order when a text is missing: this tag, its language, then English, without repeats.</summary>
    /// <returns>The distinct chain, such as <c>es-AR, es, en</c>.</returns>
    public IReadOnlyList<Locale> FallbackChain() =>
        new[] { this, Language, English }.Distinct().ToArray();

    /// <summary>Compares the canonical tags only; <see cref="Language"/> derives from the tag and can be this instance.</summary>
    /// <param name="other">The other locale.</param>
    /// <returns>Whether both tags are equal.</returns>
    public bool Equals(Locale? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    private static bool IsScript(string subtag) =>
        subtag.Length == ScriptLength && subtag.All(char.IsAsciiLetter);

    private static bool IsRegion(string subtag) =>
        (subtag.Length == RegionLetters && subtag.All(char.IsAsciiLetter))
        || (subtag.Length == RegionDigits && subtag.All(char.IsAsciiDigit));

    private static bool IsVariant(string subtag) =>
        subtag.All(char.IsAsciiLetterOrDigit)
        && (
            subtag.Length is >= MinLetterVariantLength and <= MaxVariantLength
            || (subtag.Length == DigitVariantLength && char.IsAsciiDigit(subtag[0]))
        );
}
