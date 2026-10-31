using System.Text.Json;
using System.Text.Json.Serialization;
using Frappe.Kernel.Domain;

namespace Frappe.Platform.Serialization;

/// <summary>Creates an <see cref="EntityIdJsonConverter{T}"/> for every typed id, the Kernel's and each module's.</summary>
internal sealed class EntityIdJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsValueType
        && Array.Exists(
            typeToConvert.GetInterfaces(),
            contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IEntityId<>)
                && contract.GenericTypeArguments[0] == typeToConvert
        );

    public override JsonConverter CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        (JsonConverter)(
            Activator.CreateInstance(typeof(EntityIdJsonConverter<>).MakeGenericType(typeToConvert))
            ?? throw new InvalidOperationException($"No converter for {typeToConvert}.")
        );
}
