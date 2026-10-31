using System.Text.Json;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace Frappe.Platform.Serialization;

/// <summary>JSON support for the Kernel types, which stay free of any serializer.</summary>
public static class KernelJsonExtensions
{
    /// <summary>
    /// The one registration of the Kernel wire forms, shared by the HTTP JSON options, the Wolverine serializer and
    /// the test hosts: every typed id as its UUID string, read through its <c>From</c> (invalid input throws
    /// <see cref="JsonException"/>), and NodaTime types through the official converters (instants in UTC with
    /// <c>Z</c>, <c>LocalDate</c> as <c>yyyy-MM-dd</c>).
    /// </summary>
    /// <param name="options">The options to extend.</param>
    /// <returns>The same options.</returns>
    public static JsonSerializerOptions AddKernelJson(this JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Converters.Add(new EntityIdJsonConverterFactory());
        return options.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
    }
}
