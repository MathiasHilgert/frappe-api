using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Frappe.Localization;
using Jeffijoe.MessageFormat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Frappe.Http;

/// <summary>
/// The messages contributed by each module's HTTP adapter: one JSON file per language in <c>Resources/Messages</c>
/// (<c>en.json</c>, <c>es.json</c>, <c>es-AR.json</c>, ...), embedded in the assembly, mapping error codes to ICU
/// MessageFormat patterns.
/// </summary>
public sealed class ErrorMessageOptions
{
    /// <summary>Prefix of the embedded message files; set for every project in <c>Directory.Build.props</c>.</summary>
    internal const string ResourcePrefix = "frappe.messages/";

    internal Dictionary<LanguageTag, Dictionary<string, string>> PatternsByLanguage { get; } = [];

    /// <summary>
    /// Adds the messages embedded in a module's assembly. Every file must be a supported language, every key must
    /// exist in English (the last fallback) and belong to one module only, so a typo or a clash fails at startup
    /// instead of in a response.
    /// </summary>
    public void Add(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var files = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
            .ToDictionary(name => name[ResourcePrefix.Length..^".json".Length], name => Read(assembly, name), StringComparer.Ordinal);
        Add(assembly.GetName().Name ?? "an assembly", files);
    }

    /// <summary>Adds one owner's messages, keyed by the language of each file.</summary>
    internal void Add(string owner, IReadOnlyDictionary<string, Dictionary<string, string>> filesByLanguage)
    {
        var files = filesByLanguage.ToDictionary(file => Language(owner, file.Key), file => file.Value);
        if (!files.TryGetValue(LanguageFallback.English, out var english))
        {
            throw new InvalidOperationException($"{owner} has no English messages ({ResourcePrefix}en.json).");
        }

        foreach (var (language, patterns) in files)
        {
            if (patterns.Keys.FirstOrDefault(key => !english.ContainsKey(key)) is { } untranslated)
            {
                throw new InvalidOperationException($"Message '{untranslated}' of {owner} exists in '{language}' but not in English.");
            }

            if (!PatternsByLanguage.TryGetValue(language, out var existing))
            {
                existing = new Dictionary<string, string>(StringComparer.Ordinal);
                PatternsByLanguage[language] = existing;
            }

            foreach (var (key, pattern) in patterns)
            {
                if (!existing.TryAdd(key, pattern))
                {
                    throw new InvalidOperationException($"Message '{key}' is registered twice for language '{language}'.");
                }
            }
        }
    }

    private static LanguageTag Language(string owner, string raw) =>
        LanguageTag.Create(raw) is { IsSuccess: true } tag && LanguageFallback.Supported.Contains(tag.Value)
            ? tag.Value
            : throw new InvalidOperationException(
                $"{owner} has messages for '{raw}', which is not a supported language ({string.Join(", ", LanguageFallback.Supported)}).");

    private static Dictionary<string, string> Read(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"{resourceName} of {assembly.GetName().Name} is empty.");
    }
}

/// <summary>
/// Renders error codes as text following the platform language rule (<see cref="LanguageFallback.For"/>):
/// <c>es-AR</c>, then <c>es</c>, then English. Patterns are ICU MessageFormat, so plurals and numbers follow the rules
/// of the language each text is written in, and the caller always learns which language that was.
/// </summary>
public sealed class ErrorMessageCatalog(IOptions<ErrorMessageOptions> options)
{
    private static readonly MessageFormatter Formatter = new(useCache: true);

    private readonly Dictionary<LanguageTag, Dictionary<string, string>> _patterns = options.Value.PatternsByLanguage;

    /// <summary>Returns the rendered text and the language it is written in, or <c>null</c> when no language has it.</summary>
    public (string Text, LanguageTag Language)? Render(string key, LanguageTag requestedLanguage, IReadOnlyDictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(requestedLanguage);
        ArgumentNullException.ThrowIfNull(parameters);

        foreach (var candidate in LanguageFallback.For(requestedLanguage))
        {
            if (_patterns.TryGetValue(candidate, out var patterns) && patterns.TryGetValue(key, out var pattern))
            {
                var culture = CultureInfo.GetCultureInfo(candidate.Value);
                return (Formatter.FormatMessage(pattern, Arguments(parameters, culture), culture), candidate);
            }
        }

        return null;
    }

    /// <summary>ICU has no list arguments: a sequence is joined in the culture of the message.</summary>
    private static Dictionary<string, object?> Arguments(IReadOnlyDictionary<string, object?> parameters, CultureInfo culture) =>
        parameters.ToDictionary(
            entry => entry.Key,
            entry => entry.Value is IEnumerable items and not string
                ? string.Join(", ", items.Cast<object?>().Select(item => Convert.ToString(item, culture)))
                : entry.Value,
            StringComparer.Ordinal);
}

public static class ErrorMessageRegistration
{
    /// <summary>
    /// Registers the messages embedded in a module's assembly. Modules call it from their HTTP adapter; the platform
    /// messages are registered the first time it is called.
    /// </summary>
    public static IServiceCollection AddErrorMessages(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);

        if (!services.Any(d => d.ServiceType == typeof(ErrorMessageCatalog)))
        {
            // ValidateOnStart builds the catalog options when the host starts, so a duplicated or untranslated key
            // fails the deployment instead of the first error response in production.
            services.AddOptions<ErrorMessageOptions>().ValidateOnStart()
                .Configure(options => options.Add(typeof(ErrorMessageCatalog).Assembly));
            services.TryAddSingleton<ErrorMessageCatalog>();
        }

        services.Configure<ErrorMessageOptions>(options => options.Add(assembly));
        return services;
    }
}

/// <summary>Keys of the texts the platform itself renders in Problem Details.</summary>
internal static class ProblemTexts
{
    public const string InvalidValue = "problem.invalid_value";
    public const string RuleViolation = "problem.rule_violation";
    public const string NotFound = "problem.not_found";
    public const string Conflict = "problem.conflict";
    public const string Several = "problem.several";
    public const string RateLimited = "problem.rate_limited";
    public const string MethodNotAllowed = "problem.method_not_allowed";
    public const string BadRequest = "problem.bad_request";
    public const string RequestTooLarge = "problem.request_too_large";
    public const string UnsupportedMediaType = "problem.unsupported_media_type";
    public const string Unexpected = "problem.unexpected";
    public const string Unavailable = "problem.unavailable";
}
