using System.Text.Json;
using System.Text.Json.Serialization;
using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Infrastructure.Serialization;

/// <summary>
/// The JSON form of geo's published value objects: each is written as its primitive and read back through its
/// <c>Create</c>, so an invalid value fails as a <see cref="JsonException"/> naming the rule's code. For response DTOs,
/// Contracts records and messages; request DTOs stay primitive.
/// </summary>
internal static class GeoJsonConverters
{
    public static void Add(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Converters.Add(new Text<CountryCode>(CountryCode.Create));
        options.Converters.Add(new Text<CurrencyCode>(CurrencyCode.Create));
        options.Converters.Add(new Text<SubdivisionCode>(SubdivisionCode.Create));
        options.Converters.Add(new Text<PostalCode>(PostalCode.Create));
        options.Converters.Add(new Text<TimeZoneId>(TimeZoneId.Create));
        options.Converters.Add(new Text<Locale>(Locale.Create));
        options.Converters.Add(new Digits());
    }

    // The codes stay in the message so a malformed-request problem can name the rule that failed.
    private static JsonException Rejected(IReadOnlyList<Error> errors) =>
        new(string.Join("; ", errors.Select(static error => error.Code)));

    // A value object written as its string Value; ToString returns it for every one of them.
    private sealed class Text<T>(Func<string?, ErrorOr<T>> create) : JsonConverter<T>
        where T : class
    {
        public override T Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"A {typeof(T).Name} must be a JSON string.");
            }

            ErrorOr<T> result = create(reader.GetString());
            return result.IsError ? throw Rejected(result.Errors) : result.Value;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    private sealed class Digits : JsonConverter<MinorUnit>
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
            return result.IsError ? throw Rejected(result.Errors) : result.Value;
        }

        public override void Write(
            Utf8JsonWriter writer,
            MinorUnit value,
            JsonSerializerOptions options
        ) => writer.WriteNumberValue(value.Value);
    }
}
