namespace Frappe.Localization;

/// <summary>
/// The closed list of languages a text may be written in: every language, optionally with a script and a region, that .NET
/// defines a culture for (<c>es</c>, <c>es-AR</c>, <c>zh-Hant-TW</c>), plus the names .NET resolves as aliases and clients send
/// (<c>zh-CN</c>, <c>zh-TW</c>, <c>sr-RS</c>), each in the spelling <see cref="LanguageTag"/> normalizes to. The list is
/// committed (<c>KnownLanguages.Tags.cs</c>, generated) rather than read from the host's ICU data at run time, so every host and every
/// version of the runtime accepts and rejects the same tags; a language is added by regenerating the file.
/// </summary>
public static partial class KnownLanguages
{
    // Built once, before the two views of it below (static fields initialize in textual order).
    private static readonly string[] Sorted = Names();

    private static readonly HashSet<string> Known = new(Sorted, StringComparer.Ordinal);

    /// <summary>Every known language, in ordinal order.</summary>
    public static IReadOnlyList<LanguageTag> All { get; } = [.. Sorted.Select(name => LanguageTag.Create(name).Value)];

    /// <summary>Whether <paramref name="tag"/>, already normalized, is one of the known languages.</summary>
    public static bool Contains(LanguageTag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return Known.Contains(tag.Value);
    }

    private static string[] Names() =>
        [.. $"{Tags} {Aliases}".Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).OrderBy(name => name, StringComparer.Ordinal)];
}
