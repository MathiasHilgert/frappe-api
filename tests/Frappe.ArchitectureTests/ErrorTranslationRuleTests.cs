using System.Reflection;
using ErrorOr;
using Frappe.Platform.Http.Localization;

namespace Frappe.ArchitectureTests;

// Clients localize by code, so a code shipped without a text in every supported locale would reach them in English.
public sealed class ErrorTranslationRuleTests
{
    // Every Frappé assembly the Host ships, found by file so a new module is covered at once.
    private static readonly Assembly[] _shipped =
    [
        .. Directory
            .GetFiles(AppContext.BaseDirectory, "Frappe.*.dll")
            .Select(static file => Path.GetFileNameWithoutExtension(file))
            .Where(static name =>
                !name.EndsWith("Tests", StringComparison.Ordinal)
                && !string.Equals(name, "Frappe.TestUtilities", StringComparison.Ordinal)
            )
            .Order(StringComparer.Ordinal)
            .Select(static name => Assembly.Load(name)),
    ];

    private static readonly ErrorTexts _texts = new(_shipped);

    public static TheoryData<string> SupportedLocales => ["es", "en", "pt"];

    [Theory]
    [MemberData(nameof(SupportedLocales))]
    public void Every_error_code_has_a_text_in_each_supported_locale(string locale)
    {
        string[] missing =
        [
            .. ErrorCodes()
                .Where(code => !_texts.Translates(code, locale))
                .Order(StringComparer.Ordinal),
        ];

        Assert.Empty(missing);
    }

    // Every static Error property or field of a public or internal catalog.
    private static IEnumerable<string> ErrorCodes() =>
        _shipped
            .SelectMany(static assembly => assembly.GetTypes())
            .SelectMany(static type =>
                type.GetMembers(
                    BindingFlags.Static
                        | BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.DeclaredOnly
                )
            )
            .Select(static member =>
                member switch
                {
                    PropertyInfo { PropertyType: var type } property when type == typeof(Error) =>
                        property.GetValue(null),
                    FieldInfo { FieldType: var type } field when type == typeof(Error) =>
                        field.GetValue(null),
                    _ => null,
                }
            )
            .OfType<Error>()
            .Select(static error => error.Code)
            .Distinct(StringComparer.Ordinal);
}
