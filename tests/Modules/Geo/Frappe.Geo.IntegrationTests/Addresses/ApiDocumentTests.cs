using System.Net;
using System.Text.Json;
using Frappe.Geo.Application.BusinessDays;
using Frappe.Geo.Domain.BusinessDays;
using Frappe.Geo.IntegrationTests.Hosts;
using Frappe.Geo.IntegrationTests.Persistence;
using Microsoft.AspNetCore.TestHost;

namespace Frappe.Geo.IntegrationTests.Addresses;

/// <summary>
/// What the served OpenAPI document says about parameters and responses, beyond the generator's defaults: query values
/// bind as text so the use cases answer their own errors, yet the document types them, marks the required ones,
/// declares Location on 201/202, 415 where a body is read, no-store where answers are personal, and no 422 that cannot happen.
/// </summary>
[ClassDataSource<GeoDatabase, ValkeyCache>(Shared = [SharedType.PerClass, SharedType.PerTestSession])]
public sealed class ApiDocumentTests(GeoDatabase database, ValkeyCache valkey)
{
    private async Task<JsonElement> DocumentAsync()
    {
        var answer = await GeoApi.GetAsync(database, valkey, "/openapi/v1.json");
        await Assert.That(answer.Status).IsEqualTo(HttpStatusCode.OK);
        return answer.Body;
    }

    private static JsonElement Operation(JsonElement document, string path, string method) =>
        document.GetProperty("paths").GetProperty(path).GetProperty(method);

    private static JsonElement Parameter(JsonElement operation, string name) =>
        operation.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == name && p.GetProperty("in").GetString() == "query");

    private static bool IsRequired(JsonElement parameter) => parameter.TryGetProperty("required", out var required) && required.GetBoolean();

    private static string? SchemaValue(JsonElement parameter, string member) =>
        parameter.GetProperty("schema").TryGetProperty(member, out var value) ? value.ToString() : null;

    [Test]
    public async Task Required_query_parameters_are_marked_required_and_typed()
    {
        var document = await DocumentAsync();
        var businessDays = Operation(document, "/v1/geo/countries/{countryCode}/business-days", "get");
        var suggestions = Operation(document, "/v1/geo/address-suggestions", "get");
        var remove = Operation(document, "/v1/geo/countries/{countryCode}/holidays/overrides", "delete");

        var policy = Parameter(businessDays, "policy");
        await Assert.That(IsRequired(policy)).IsTrue();
        await Assert.That(policy.GetProperty("schema").GetProperty("enum").EnumerateArray().Select(v => v.GetString()!))
            .IsEquivalentTo([.. Enum.GetValues<BusinessDayPolicy>().Select(BusinessDayPolicies.NameOf)]);
        await Assert.That(IsRequired(Parameter(businessDays, "from"))).IsTrue();
        await Assert.That(SchemaValue(Parameter(businessDays, "from"), "format")).IsEqualTo("date");
        await Assert.That(IsRequired(Parameter(businessDays, "count"))).IsTrue();
        await Assert.That(SchemaValue(Parameter(businessDays, "count"), "type")).IsEqualTo("integer");
        await Assert.That(IsRequired(Parameter(Operation(document, "/v1/geo/countries/{countryCode}/calendar/{date}", "get"), "policy"))).IsTrue();

        await Assert.That(IsRequired(Parameter(suggestions, "q"))).IsTrue();
        await Assert.That(IsRequired(Parameter(suggestions, "session"))).IsTrue();
        await Assert.That(SchemaValue(Parameter(suggestions, "session"), "format")).IsEqualTo("uuid");
        await Assert.That(IsRequired(Parameter(suggestions, "country"))).IsFalse();

        await Assert.That(IsRequired(Parameter(remove, "date"))).IsTrue();
        await Assert.That(SchemaValue(Parameter(remove, "date"), "format")).IsEqualTo("date");
        await Assert.That(IsRequired(Parameter(remove, "name"))).IsTrue();
        await Assert.That(IsRequired(Parameter(remove, "scope"))).IsFalse();
        await Assert.That(SchemaValue(Parameter(remove, "scope"), "pattern")).IsNotNull();
    }

    [Test]
    public async Task Optional_numbers_are_typed_as_integers()
    {
        var document = await DocumentAsync();

        var year = Parameter(Operation(document, "/v1/geo/countries/{countryCode}/holidays", "get"), "year");
        var level = Parameter(Operation(document, "/v1/geo/subdivisions/{subdivisionCode}/areas", "get"), "level");
        var limit = Parameter(Operation(document, "/v1/geo/subdivisions/{subdivisionCode}/localities", "get"), "limit");

        await Assert.That(IsRequired(year)).IsFalse();
        await Assert.That(SchemaValue(year, "type")).IsEqualTo("integer");
        await Assert.That(SchemaValue(year, "minimum")).IsEqualTo("1900");
        await Assert.That(SchemaValue(year, "maximum")).IsEqualTo("2100");
        await Assert.That(SchemaValue(level, "type")).IsEqualTo("integer");
        await Assert.That(SchemaValue(limit, "type")).IsEqualTo("integer");
    }

    [Test]
    public async Task Every_201_and_202_declares_its_Location()
    {
        var document = await DocumentAsync();

        var created = document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .SelectMany(operation => operation.Value.GetProperty("responses").EnumerateObject())
            .Where(response => response.Name is "201" or "202")
            .ToArray();

        await Assert.That(created).IsNotEmpty();
        await Assert.That(created.All(response => response.Value.TryGetProperty("headers", out var headers) && headers.TryGetProperty("Location", out _))).IsTrue();
    }

    [Test]
    public async Task Operations_that_read_a_body_declare_415_and_only_suppressions_declare_422()
    {
        var document = await DocumentAsync();

        var withBody = document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Where(operation => operation.Value.TryGetProperty("requestBody", out _))
            .ToArray();
        var overrides = Operation(document, "/v1/geo/countries/{countryCode}/holidays/overrides", "put");
        string[] suppressions = ["/v1/geo/countries/{countryCode}/holidays/suppressions", "/v1/geo/subdivisions/{subdivisionCode}/holidays/suppressions"];

        await Assert.That(withBody).IsNotEmpty();
        await Assert.That(withBody.All(operation => operation.Value.GetProperty("responses").TryGetProperty("415", out _))).IsTrue();
        await Assert.That(overrides.GetProperty("responses").TryGetProperty("422", out _)).IsFalse();
        foreach (var path in suppressions)
        {
            // geo.observance.not_in_source: only an observance the source publishes can be suppressed.
            await Assert.That(Operation(document, path, "put").GetProperty("responses").TryGetProperty("422", out _)).IsTrue().Because(path);
        }
    }

    [Test]
    [Arguments("ObservanceOverrideRequest", new[] { "date", "name", "kind" })]
    [Arguments("LegalBasisRequest", new[] { "citation" })]
    [Arguments("AddressValidationRequest", new[] { "reference", "session" })]
    public async Task Request_bodies_mark_only_their_required_members_required(string schema, string[] required)
    {
        var document = await DocumentAsync();

        var members = document.GetProperty("components").GetProperty("schemas").GetProperty(schema)
            .GetProperty("required").EnumerateArray().Select(member => member.GetString()!);

        await Assert.That(members).IsEquivalentTo(required);
    }

    [Test]
    [Arguments("/v1/geo/address-suggestions")]
    [Arguments("/v1/geo/holiday-syncs")]
    [Arguments("/v1/geo/subdivisions/{subdivisionCode}/aliases")]
    public async Task Personal_and_administration_answers_declare_no_store(string path)
    {
        var document = await DocumentAsync();

        var ok = Operation(document, path, "get").GetProperty("responses").GetProperty("200");

        await Assert.That(ok.GetProperty("headers").GetProperty("Cache-Control").GetProperty("description").GetString()).Contains("no-store");
        await Assert.That(ok.GetProperty("headers").TryGetProperty("ETag", out _)).IsFalse();
    }

    [Test]
    public async Task An_administration_read_answers_no_store_without_a_validator()
    {
        var answer = await GeoApi.GetAsync(database, valkey, "/v1/geo/holiday-syncs");

        await Assert.That(answer.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(answer.CacheControl).IsEqualTo("no-store");
    }

    [Test]
    public async Task The_document_itself_carries_no_caching_headers()
    {
        await using var app = await GeoApi.StartAsync(database, valkey);
        using var response = await app.GetTestClient().GetAsync("/openapi/v1.json");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl).IsNull();
        await Assert.That(response.Headers.ETag).IsNull();
    }
}
