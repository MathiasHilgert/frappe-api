using System.Net;
using System.Text.Json;

namespace Frappe.Api.IntegrationTests.Contracts;

/// <summary>HTTP-02: a module owns its routes, so two modules (OpenAPI tags) never share the first path segment after <c>/v1</c>.</summary>
public sealed class RouteOwnershipTests
{
    [Test]
    public async Task No_two_modules_own_routes_under_the_same_first_segment_of_what_the_Api_serves()
    {
        // Act
        var (status, document) = await OpenApiContractTests.ServeAsync();
        var owners = OwnersBySegment(document);
        var shared = SharedSegments(owners);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(shared).IsEmpty().Because("each module owns its own first path segment (HTTP-02)");
        }
    }

    [Test]
    public async Task A_document_where_two_tags_share_a_first_segment_is_reported()
    {
        // Arrange
        using var parsed = JsonDocument.Parse("""
            {"paths":{
              "/v1/x/a":{"get":{"tags":["One"]}},
              "/v1/x/b":{"post":{"tags":["Two"]}},
              "/v1/y":{"get":{"tags":["One"]}}}}
            """);

        // Act
        var shared = SharedSegments(OwnersBySegment(parsed.RootElement));

        // Assert
        await Assert.That(shared).IsEquivalentTo(["/v1/x: One, Two"]);
    }

    [Test]
    public async Task A_document_where_each_tag_owns_its_own_segment_reports_nothing()
    {
        // Arrange
        using var parsed = JsonDocument.Parse("""
            {"paths":{
              "/v1/x":{"get":{"tags":["One"]},"post":{"tags":["One"]}},
              "/v1/y":{"get":{"tags":["Two"]}}}}
            """);

        // Act
        var owners = OwnersBySegment(parsed.RootElement);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(owners).Count().IsEqualTo(2);
            await Assert.That(SharedSegments(owners)).IsEmpty();
        }
    }

    private static Dictionary<string, string[]> OwnersBySegment(JsonElement document) =>
        document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .SelectMany(operation => operation.Value.GetProperty("tags").EnumerateArray())
                .Select(tag => (
                    Segment: path.Name.Split('/', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "",
                    Tag: tag.GetString()!)))
            .GroupBy(entry => entry.Segment, entry => entry.Tag)
            .ToDictionary(group => group.Key, group => group.Distinct().Order(StringComparer.Ordinal).ToArray());

    private static string[] SharedSegments(Dictionary<string, string[]> owners) =>
        [.. owners.Where(entry => entry.Value.Length > 1).Select(entry => $"/v1/{entry.Key}: {string.Join(", ", entry.Value)}")];
}
