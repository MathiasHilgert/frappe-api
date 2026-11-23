using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Frappe.Persistence;

/// <summary>
/// How a closed set of <typeparamref name="TEnum"/> is stored (DATA-06): short lower snake_case text (<c>EmailChange</c> is
/// <c>email_change</c>) guarded by a <c>CHECK</c>. The converter, the <c>CHECK</c> list and the column length all read the
/// same members, so a new member needs no second edit and a renamed one shows in the model. The stored text is the member's
/// name: a test per enum pins its spelling, so renaming a member cannot silently change what a database holds.
/// </summary>
/// <typeparam name="TEnum">The enum stored as text; its members are the closed set.</typeparam>
[SuppressMessage("Design", "CA1000", Justification = "DATA-06 reads StoredEnum<Kind>.CheckList: one source per enum, no instance to carry.")]
[SuppressMessage("Naming", "CA1711", Justification = "The name is the DATA-06 term: the stored form of an enum, not an enum itself.")]
[SuppressMessage("Major Code Smell", "S2743", Justification = "One set of texts per closed enum type is the point; built once, shared.")]
public static class StoredEnum<TEnum>
    where TEnum : struct, Enum
{
    private static readonly Dictionary<TEnum, string> Texts = Enum.GetValues<TEnum>()
        .Distinct()
        .ToDictionary(value => value, value => JsonNamingPolicy.SnakeCaseLower.ConvertName(Enum.GetName(value)!));

    private static readonly Dictionary<string, TEnum> Values = Texts.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    /// <summary>The text of every member in declaration order, each quoted, as the list of a <c>col IN (...)</c> check.</summary>
    public static string CheckList { get; } = string.Join(", ", Enum.GetValues<TEnum>().Distinct().Select(value => $"'{Texts[value]}'"));

    /// <summary>The length of the longest stored text, for the column's maximum length.</summary>
    public static int MaxLength { get; } = Texts.Values.Max(text => text.Length);

    /// <summary>Writes a member as its text and reads the text back as the member.</summary>
    public static ValueConverter<TEnum, string> Converter { get; } = new(value => ToText(value), text => FromText(text));

    /// <summary>The stored text of <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is no member of the enum.</exception>
    public static string ToText(TEnum value) =>
        Texts.TryGetValue(value, out var text)
            ? text
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"'{value}' is no member of {typeof(TEnum).Name}.");

    /// <summary>The member stored as <paramref name="text"/>.</summary>
    /// <exception cref="InvalidOperationException">No member is stored as <paramref name="text"/>.</exception>
    public static TEnum FromText(string text) =>
        Values.TryGetValue(text, out var value)
            ? value
            : throw new InvalidOperationException($"'{text}' is stored for no member of {typeof(TEnum).Name}.");
}
