using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Frappe.Kernel.Domain;
using Frappe.Platform.Serialization;

namespace Frappe.ArchitectureTests;

// A typed id reaches clients as a UUID string and comes back in a body, a route or a query string, so every typed id
// any Frappé assembly ships must survive all three without a converter or binder of its own. This test assembly is
// scanned too, so RequestRuleTests.OwnerId keeps the rule biting while no shipped assembly declares a typed id.
public sealed class EntityIdRuleTests
{
    // Every Frappé assembly the Host ships, found by file so a new module is covered at once.
    private static readonly Assembly[] _shipped =
    [
        .. Directory
            .GetFiles(AppContext.BaseDirectory, "Frappe.*.dll")
            .Select(static file => Path.GetFileNameWithoutExtension(file))
            .Where(static name =>
                !name.EndsWith("Tests", StringComparison.Ordinal)
                && !string.Equals(name, "Frappe.TestUtilities", StringComparison.Ordinal)
            )
            .Order(StringComparer.Ordinal)
            .Select(static name => Assembly.Load(name)),
    ];

    private static readonly JsonSerializerOptions _json =
        new JsonSerializerOptions().AddKernelJson();

    private static readonly MethodInfo _roundTrip =
        typeof(EntityIdRuleTests).GetMethod(
            nameof(RoundTrip),
            BindingFlags.NonPublic | BindingFlags.Static
        ) ?? throw new InvalidOperationException("RoundTrip is missing.");

    [Fact]
    public void Every_typed_id_round_trips_through_json_and_parameter_binding()
    {
        Type[] ids =
        [
            .. _shipped
                .Append(typeof(EntityIdRuleTests).Assembly)
                .SelectMany(static assembly => assembly.GetTypes())
                .Where(static type =>
                    type.IsValueType
                    && Array.Exists(
                        type.GetInterfaces(),
                        contract =>
                            contract.IsGenericType
                            && contract.GetGenericTypeDefinition() == typeof(IEntityId<>)
                            && contract.GenericTypeArguments[0] == type
                    )
                ),
        ];

        Assert.NotEmpty(ids);
        foreach (Type id in ids)
        {
            _roundTrip.MakeGenericMethod(id).Invoke(null, null);
        }
    }

    private static void RoundTrip<T>()
        where T : struct, IEntityId<T>
    {
        T id = T.NewId();
        string text = id.Value.ToString();

        string json = JsonSerializer.Serialize(id, _json);
        Assert.Equal($"\"{text}\"", json);
        Assert.Equal(id, JsonSerializer.Deserialize<T>(json, _json));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<T>($"\"{Guid.Empty}\"", _json)
        );

        // Minimal APIs bind a route or query parameter through a public static TryParse declared on the type.
        Assert.NotNull(
            typeof(T).GetMethod(
                nameof(IParsable<T>.TryParse),
                BindingFlags.Public | BindingFlags.Static,
                [typeof(string), typeof(IFormatProvider), typeof(T).MakeByRefType()]
            )
        );
        Assert.True(T.TryParse(text, CultureInfo.InvariantCulture, out T parsed));
        Assert.Equal(id, parsed);
        Assert.False(T.TryParse(Guid.Empty.ToString(), CultureInfo.InvariantCulture, out _));
        Assert.False(T.TryParse("not-a-uuid", CultureInfo.InvariantCulture, out _));
        Assert.Equal(id, T.Parse(text, CultureInfo.InvariantCulture));
    }
}
