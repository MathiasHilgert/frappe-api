using System.Net;
using System.Text;
using System.Text.Json;
using Frappe.Testing;
using Microsoft.AspNetCore.TestHost;

namespace Frappe.Api.IntegrationTests.Contracts;

/// <summary>
/// The committed HTTP contract is exactly what the Api serves, composed as deployed. CI diffs it against the base
/// branch, so a breaking change needs "!" in the title. After a deliberate change, run <c>just contracts update</c> and
/// commit the file.
/// </summary>
public sealed class OpenApiContractTests
{
    private const string Contract = "contracts/openapi/v1.json";

    /// <summary>Readable diffs: indented, and non-ASCII text kept as written.</summary>
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Test]
    public async Task The_committed_HTTP_contract_is_what_the_Api_serves()
    {
        // Act
        var (status, document) = await ServeAsync();
        var served = JsonSerializer.Serialize(document, Indented) + "\n";
        var path = Path.Combine(RepositoryRoot.FullPath, Contract);
        if (Environment.GetEnvironmentVariable("FRAPPE_UPDATE_CONTRACTS") == "true")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, served, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        var committed = File.Exists(path) ? (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n") : "";

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(committed).IsEqualTo(served).Because($"{Contract} is out of date: run `just contracts update` and commit it");
        }
    }

    /// <summary>The real Api (its module lists, its settings file and a deploy's secrets), asked for its document; nothing connects.</summary>
    internal static async Task<(HttpStatusCode Status, JsonElement Document)> ServeAsync()
    {
        await using var app = await ApiHost.StartAsync([], Modules.Endpoints, ApiHost.Deployed);
        using var response = await app.GetTestClient().GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, document.RootElement.Clone());
    }
}
