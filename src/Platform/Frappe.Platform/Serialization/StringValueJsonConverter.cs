using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;

namespace Frappe.Platform.Serialization;

/// <summary>Writes a Kernel value object as its JSON string and reads it back through the value object's <c>Create</c>.</summary>
/// <typeparam name="T">The value object type.</typeparam>
internal abstract class StringValueJsonConverter<T> : JsonConverter<T>
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

        ErrorOr<T> result = Create(reader.GetString());
        return result.IsError ? throw JsonErrors.From(result.Errors) : result.Value;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Format(value));

    protected abstract ErrorOr<T> Create(string? value);

    protected abstract string Format(T value);
}
