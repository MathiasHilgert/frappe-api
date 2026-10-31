using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;
using NodaTime.Text;

namespace Frappe.Platform.Http;

/// <summary>Writes a <see cref="LocalDate"/> as an ISO <c>yyyy-MM-dd</c> string and reads it back.</summary>
internal sealed class LocalDateJsonConverter : JsonConverter<LocalDate>
{
    public override LocalDate Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("A date must be a JSON string in the yyyy-MM-dd form.");
        }

        // GetString also reads a value split across segments.
        ParseResult<LocalDate> result = LocalDatePattern.Iso.Parse(
            reader.GetString() ?? string.Empty
        );
        return result.Success
            ? result.Value
            : throw new JsonException("A date must be a JSON string in the yyyy-MM-dd form.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        LocalDate value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(LocalDatePattern.Iso.Format(value));
}
