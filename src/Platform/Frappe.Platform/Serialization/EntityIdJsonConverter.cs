using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Frappe.Kernel.Domain;

namespace Frappe.Platform.Serialization;

/// <summary>Writes a typed id as its UUID string and reads it back through its <c>From</c>.</summary>
/// <typeparam name="T">The typed id.</typeparam>
internal sealed class EntityIdJsonConverter<T> : JsonConverter<T>
    where T : struct, IEntityId<T>
{
    public override T Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.String || !reader.TryGetGuid(out Guid value))
        {
            throw new JsonException($"A {typeof(T).Name} must be a JSON UUID string.");
        }

        ErrorOr<T> result = T.From(value);
        // The codes stay in the message so a malformed-request problem can name the rule that failed.
        return result.IsError
            ? throw new JsonException(string.Join("; ", result.Errors.Select(static e => e.Code)))
            : result.Value;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
