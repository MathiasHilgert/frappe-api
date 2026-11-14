using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Frappe.IntegrationEvents;
using Frappe.Testing;

namespace Frappe.Architecture.Tests;

/// <summary>
/// Integration events are a public contract: their payload may only grow with optional fields, and any other change is
/// a new type with a new version. The committed contract describes every event's payload as an OpenAPI document (one
/// path per CloudEvents type, the payload as its response), so CI can diff it against main with the same tool as the
/// HTTP contract: a removed event, a removed field or a changed type is a breaking change.
/// </summary>
public sealed class IntegrationEventContractTests
{
    /// <summary>The committed event contract, relative to the repository root. CI diffs it against main.</summary>
    private const string Contract = "docs/api/events/v1.json";

    /// <summary>Readable diffs: indented, and non-ASCII text kept as written.</summary>
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Test]
    public async Task Every_integration_event_names_its_CloudEvents_type()
    {
        var unnamed = Events().Where(type => type.GetCustomAttribute<IntegrationEventAttribute>() is null).Select(type => type.FullName);

        await Assert.That(unnamed).IsEmpty();
    }

    /// <summary>
    /// The committed contract is exactly the payloads the outbox serializes. After a deliberate change, run
    /// <c>just contracts update</c> and commit the file.
    /// </summary>
    [Test]
    public async Task The_committed_event_contract_is_what_the_modules_publish()
    {
        var current = Document().ToJsonString(Indented) + "\n";
        var path = Path.Combine(RepositoryRoot.FullPath, Contract);

        if (Environment.GetEnvironmentVariable("FRAPPE_UPDATE_CONTRACTS") == "true")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, current, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        var committed = File.Exists(path) ? (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n") : "";
        await Assert.That(committed).IsEqualTo(current)
            .Because($"{Contract} is out of date: run `just contracts update` and commit it");
    }

    /// <summary>Every event type of every module's Contracts assembly (the Worker loads all modules).</summary>
    private static IEnumerable<Type> Events() =>
        Directory.EnumerateFiles(AppContext.BaseDirectory, "Frappe.*.Contracts.dll")
            .Select(System.Reflection.Assembly.LoadFrom)
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IIntegrationEvent).IsAssignableFrom(type));

    private static JsonObject Document()
    {
        var paths = new JsonObject();
        foreach (var (name, type) in Events()
            .Select(type => (Name: type.GetCustomAttribute<IntegrationEventAttribute>()?.Type ?? type.FullName!, Type: type))
            .OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            // The same options the outbox serializes with (OutboxInterceptor) and the inbox reads with.
            var schema = JsonSerializerOptions.Web.GetJsonSchemaAsNode(
                type, new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true });
            paths[$"/{name}"] = new JsonObject
            {
                ["get"] = new JsonObject
                {
                    ["operationId"] = type.Name,
                    ["summary"] = $"Payload (CloudEvents data) of {name}",
                    ["responses"] = new JsonObject
                    {
                        ["200"] = new JsonObject
                        {
                            ["description"] = type.FullName,
                            ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = schema } },
                        },
                    },
                },
            };
        }

        return new JsonObject
        {
            ["openapi"] = "3.1.0",
            ["info"] = new JsonObject { ["title"] = "Frappé integration events", ["version"] = "1" },
            ["paths"] = paths,
        };
    }
}
