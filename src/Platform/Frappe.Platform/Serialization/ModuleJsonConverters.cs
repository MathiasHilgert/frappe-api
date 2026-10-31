using System.Text.Json;
using System.Text.Json.Serialization;
using Frappe.Platform.Modularity;

namespace Frappe.Platform.Serialization;

/// <summary>
/// The converters every module adds through <see cref="IModule.ConfigureJson"/>, gathered on first use. The bus builds
/// its serializer options while the platform is added, before any module is, so the modules are read only when a
/// type is first serialized, after the host is composed.
/// </summary>
/// <param name="modules">Reads the added modules.</param>
internal sealed class ModuleJsonConverters(Func<IEnumerable<IModule>> modules)
    : JsonConverterFactory
{
    private readonly Lazy<JsonConverter[]> _converters = new(() =>
    {
        JsonSerializerOptions collected = new();
        foreach (IModule module in modules())
        {
            module.ConfigureJson(collected);
        }

        return [.. collected.Converters];
    });

    public override bool CanConvert(Type typeToConvert) =>
        Array.Exists(_converters.Value, converter => converter.CanConvert(typeToConvert));

    public override JsonConverter? CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        JsonConverter converter =
            Array.Find(_converters.Value, candidate => candidate.CanConvert(typeToConvert))
            ?? throw new InvalidOperationException($"No module converts {typeToConvert}.");
        return converter is JsonConverterFactory factory
            ? factory.CreateConverter(typeToConvert, options)
            : converter;
    }
}
