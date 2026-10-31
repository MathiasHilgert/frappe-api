using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Frappe.Kernel.Geography;

namespace Frappe.Platform.Serialization;

/// <summary>Writes an <see cref="Address"/> as <c>{ streetLine, cityId, postalCode }</c> and reads it back through <see cref="Address.Create"/>.</summary>
internal sealed class AddressJsonConverter : JsonConverter<Address>
{
    private const string StreetLine = "streetLine";
    private const string CityId = "cityId";
    private const string PostalCode = "postalCode";

    public override Address Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("An Address must be a JSON object.");
        }

        string? streetLine = null;
        Kernel.Geography.CityId city = default;
        Kernel.Geography.PostalCode? postalCode = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string name = reader.GetString() ?? string.Empty;
            reader.Read();
            switch (name)
            {
                case StreetLine:
                    streetLine = ReadString(ref reader, name);
                    break;
                case CityId:
                    city = new CityIdJsonConverter().Read(
                        ref reader,
                        typeof(Kernel.Geography.CityId),
                        options
                    );
                    break;
                case PostalCode:
                    postalCode = ReadPostalCode(ref reader);
                    break;
                default:
                    throw new JsonException($"An Address has no '{name}' member.");
            }
        }

        ErrorOr<Address> result = Address.Create(city, streetLine, postalCode);
        return result.IsError ? throw JsonErrors.From(result.Errors) : result.Value;
    }

    public override void Write(Utf8JsonWriter writer, Address value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(StreetLine, value.StreetLine);
        writer.WriteString(CityId, value.City.Value);
        if (value.PostalCode is not null)
        {
            writer.WriteString(PostalCode, value.PostalCode.Value);
        }

        writer.WriteEndObject();
    }

    private static string? ReadString(ref Utf8JsonReader reader, string name) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            _ => throw new JsonException($"An Address '{name}' must be a JSON string."),
        };

    private static Kernel.Geography.PostalCode? ReadPostalCode(ref Utf8JsonReader reader)
    {
        string? raw = ReadString(ref reader, PostalCode);
        if (raw is null)
        {
            return null;
        }

        ErrorOr<Kernel.Geography.PostalCode> result = Kernel.Geography.PostalCode.Create(raw);
        return result.IsError ? throw JsonErrors.From(result.Errors) : result.Value;
    }
}
