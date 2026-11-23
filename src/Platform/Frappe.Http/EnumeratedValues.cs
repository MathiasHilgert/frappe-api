using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Http;

/// <summary>
/// Writes enumerated values as <see cref="EnumeratedValue"/> (HTTP-25). Each name is a message of the owning module's
/// <c>Resources/Messages</c> under the key <c>&lt;family&gt;.&lt;code&gt;</c> (for example
/// <c>orders.status.paid</c>), with the language fallback of every other text, so a module adds a language by
/// adding a file.
/// </summary>
public static class EnumeratedValues
{
    private static readonly IReadOnlyDictionary<string, object?> NoParameters = new Dictionary<string, object?>();

    /// <summary>
    /// The value <paramref name="code"/> of the <paramref name="family"/>, named in the request language, which this
    /// response then declares in <c>Content-Language</c>. A value with no English name is a wiring mistake, never a
    /// client error, so it throws.
    /// </summary>
    public static EnumeratedValue Of(HttpContext httpContext, string family, string code)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrEmpty(family);
        ArgumentException.ThrowIfNullOrEmpty(code);

        var catalog = httpContext.RequestServices.GetRequiredService<ErrorMessageCatalog>();
        var key = $"{family}.{code}";
        if (catalog.Render(key, ServedLanguages.LanguageOf(httpContext), NoParameters) is not { } name)
        {
            throw new InvalidOperationException($"The enumerated value '{key}' has no name in any language: add it to the module's "
                + $"Resources/Messages/en.json.");
        }

        ServedLanguages.Declare(httpContext, [name.Language]);
        return new EnumeratedValue(code, name.Text);
    }

    /// <summary>Every value of <paramref name="family"/> in <paramref name="codes"/>, in order.</summary>
    public static IReadOnlyList<EnumeratedValue> Of(HttpContext httpContext, string family, IEnumerable<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        return [.. codes.Select(code => Of(httpContext, family, code))];
    }
}
