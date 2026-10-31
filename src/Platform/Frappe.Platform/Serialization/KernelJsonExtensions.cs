using System.Text.Json;

namespace Frappe.Platform.Serialization;

/// <summary>JSON support for the Kernel types, which stay free of any serializer.</summary>
public static class KernelJsonExtensions
{
    /// <summary>
    /// Adds a converter for every typed id: written as its UUID string and read through its <c>From</c>, so invalid
    /// input throws <see cref="JsonException"/>.
    /// </summary>
    /// <param name="options">The options to extend.</param>
    /// <returns>The same options.</returns>
    public static JsonSerializerOptions AddKernelValueObjectConverters(
        this JsonSerializerOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Converters.Add(new EntityIdJsonConverterFactory());
        return options;
    }
}
