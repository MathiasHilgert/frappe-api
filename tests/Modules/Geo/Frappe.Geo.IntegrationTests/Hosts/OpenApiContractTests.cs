using System.Net;
using System.Text;
using System.Text.Json;
using Frappe.Geo.IntegrationTests.Persistence;
using Frappe.Testing;

namespace Frappe.Geo.IntegrationTests.Hosts;

/// <summary>
/// The committed HTTP contract is exactly what the Api serves. CI diffs it against the base branch, so a breaking change
/// needs "!" in the title. After a deliberate change, run <c>just contracts update</c> and commit the file.
/// </summary>
[ClassDataSource<GeoDatabase, ValkeyCache>(Shared = [SharedType.PerClass, SharedType.PerTestSession])]
public sealed class OpenApiContractTests(GeoDatabase database, ValkeyCache valkey)
{
    private const string Contract = "docs/api/openapi/v1.json";

    /// <summary>Readable diffs: indented, and non-ASCII text kept as written.</summary>
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Test]
    public async Task The_committed_HTTP_contract_is_what_the_Api_serves()
    {
        var answer = await GeoApi.GetAsync(database, valkey, "/openapi/v1.json");
        await Assert.That(answer.Status).IsEqualTo(HttpStatusCode.OK);

        var served = JsonSerializer.Serialize(answer.Body, Indented) + "\n";
        var path = Path.Combine(RepositoryRoot.FullPath, Contract);
        if (Environment.GetEnvironmentVariable("FRAPPE_UPDATE_CONTRACTS") == "true")
        {
            await File.WriteAllTextAsync(path, served, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        var committed = File.Exists(path) ? (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n") : "";
        await Assert.That(committed).IsEqualTo(served).Because($"{Contract} is out of date: run `just contracts update` and commit it");
    }
}
