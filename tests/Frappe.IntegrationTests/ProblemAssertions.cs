using System.Text.Json;
using Alba;
using Shouldly;

namespace Frappe.IntegrationTests;

/// <summary>The one problem shape every endpoint answers a single error with (RFC 9457 plus code and errors[]).</summary>
internal static class ProblemAssertions
{
    public static async Task<JsonElement> ShouldBeProblemAsync(this IScenarioResult result, int status, string code, string? field = null)
    {
        result.Context.Response.ContentType.ShouldBe("application/problem+json");
        var body = await result.ReadAsJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().ShouldBe(status);
        body.GetProperty("type").GetString().ShouldBe($"urn:frappe:problem:{code}");
        body.GetProperty("code").GetString().ShouldBe(code);
        body.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        body.GetProperty("detail").GetString().ShouldBe(body.GetProperty("title").GetString());
        var error = body.GetProperty("errors").EnumerateArray().ShouldHaveSingleItem();
        error.GetProperty("code").GetString().ShouldBe(code);
        error.GetProperty("message").GetString().ShouldBe(body.GetProperty("title").GetString());
        if (field is null)
            error.GetProperty("field").ValueKind.ShouldBe(JsonValueKind.Null);
        else
            error.GetProperty("field").GetString().ShouldBe(field);
        return body;
    }
}
