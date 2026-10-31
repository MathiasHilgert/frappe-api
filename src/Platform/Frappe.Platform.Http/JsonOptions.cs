using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace Frappe.Platform.Http;

/// <summary>The one set of HTTP JSON options every endpoint reads and writes with.</summary>
internal static class JsonOptions
{
    public static void Configure(Microsoft.AspNetCore.Http.Json.JsonOptions options)
    {
        JsonSerializerOptions json = options.SerializerOptions;
        json.TypeInfoResolverChain.Insert(0, FrappeJsonContext.Default);
        // A misspelled request field would otherwise change nothing without telling the client.
        json.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        // Responses keep their shape: null is written, never dropped.
        json.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        // Instants in UTC with Z, LocalDate as yyyy-MM-dd: the official NodaTime converters.
        json.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
    }
}
