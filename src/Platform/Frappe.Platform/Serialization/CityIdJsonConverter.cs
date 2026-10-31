using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Frappe.Kernel.Geography;

namespace Frappe.Platform.Serialization;

internal sealed class CityIdJsonConverter : JsonConverter<CityId>
{
    public override CityId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.String || !reader.TryGetGuid(out Guid value))
        {
            throw new JsonException("A CityId must be a JSON UUID string.");
        }

        ErrorOr<CityId> result = CityId.From(value);
        return result.IsError ? throw JsonErrors.From(result.Errors) : result.Value;
    }

    public override void Write(
        Utf8JsonWriter writer,
        CityId value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value.Value);
}
