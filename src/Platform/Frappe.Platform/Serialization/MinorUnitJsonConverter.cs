using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Frappe.Kernel.Currencies;

namespace Frappe.Platform.Serialization;

internal sealed class MinorUnitJsonConverter : JsonConverter<MinorUnit>
{
    public override MinorUnit Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out int digits))
        {
            throw new JsonException("A MinorUnit must be a JSON integer.");
        }

        ErrorOr<MinorUnit> result = MinorUnit.Create(digits);
        return result.IsError ? throw JsonErrors.From(result.Errors) : result.Value;
    }

    public override void Write(
        Utf8JsonWriter writer,
        MinorUnit value,
        JsonSerializerOptions options
    ) => writer.WriteNumberValue(value.Value);
}
