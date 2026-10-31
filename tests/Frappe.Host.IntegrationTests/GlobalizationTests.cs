using System.Text.Json;

namespace Frappe.Host.IntegrationTests;

// Catalogue names are ordered with each locale's culture (Alemania before Argentina in es); invariant globalization
// would silently order them by code points instead.
public sealed class GlobalizationTests
{
    [Fact]
    public async Task The_host_keeps_culture_data()
    {
        await using FileStream config = File.OpenRead(
            Path.Combine(AppContext.BaseDirectory, "Frappe.Host.runtimeconfig.json")
        );
        using JsonDocument document = await JsonDocument.ParseAsync(
            config,
            cancellationToken: TestContext.Current.CancellationToken
        );

        bool invariant =
            document
                .RootElement.GetProperty("runtimeOptions")
                .TryGetProperty("configProperties", out JsonElement properties)
            && properties.TryGetProperty("System.Globalization.Invariant", out JsonElement setting)
            && setting.GetBoolean();

        invariant.ShouldBeFalse();
    }
}
