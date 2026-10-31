using System.Text.Json;
using Alba;

namespace Frappe.Geo.IntegrationTests;

// Reads geo responses as JSON documents, so tests assert the wire form itself.
internal static class GeoHttp
{
    public static async Task<(IScenarioResult Result, JsonElement Body)> GetAsync(
        IAlbaHost host,
        string url,
        int status
    )
    {
        IScenarioResult result = await host.Scenario(scenario =>
        {
            scenario.Get.Url(url);
            scenario.StatusCodeShouldBe(status);
        });
        string text = await result.ReadAsTextAsync();
        using var document = JsonDocument.Parse(text.Length == 0 ? "null" : text);
        return (result, document.RootElement.Clone());
    }

    public static string[] Codes(JsonElement page, string member) =>
        [
            .. page.GetProperty("items")
                .EnumerateArray()
                .Select(item => item.GetProperty(member).GetString() ?? ""),
        ];

    public static JsonElement Item(JsonElement page, string member, string value) =>
        page.GetProperty("items")
            .EnumerateArray()
            .Single(item =>
                string.Equals(item.GetProperty(member).GetString(), value, StringComparison.Ordinal)
            );

    public static JsonElement FirstError(JsonElement problem) => problem.GetProperty("errors")[0];
}
