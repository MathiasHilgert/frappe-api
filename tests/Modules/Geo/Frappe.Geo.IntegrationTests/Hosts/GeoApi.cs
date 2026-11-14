using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Frappe.Api;
using Frappe.Geo.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Frappe.Geo.IntegrationTests.Hosts;

/// <summary>Requests through the real Api (its composition, not a copy) over a migrated and seeded Geo database.</summary>
internal static class GeoApi
{
    public sealed record Answer(HttpStatusCode Status, string? ContentLanguage, JsonElement Body, string? CacheControl = null, string? Location = null, string? ETag = null);

    public static Task<Answer> GetAsync(GeoDatabase database, ValkeyCache valkey, string path, string? acceptLanguage = null) =>
        SendAsync(database, valkey, HttpMethod.Get, path, body: null, acceptLanguage);

    /// <summary>Any request; <paramref name="body"/>, when given, is sent as JSON.</summary>
    public static async Task<Answer> SendAsync(GeoDatabase database, ValkeyCache valkey, HttpMethod method, string path, object? body = null, string? acceptLanguage = null)
    {
        await using var app = await StartAsync(database, valkey);
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        using var response = await app.GetTestClient().SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content.Length == 0 ? "null" : content);
        var language = response.Content.Headers.ContentLanguage.Count == 0 ? null : string.Join(", ", response.Content.Headers.ContentLanguage);
        await app.StopAsync();
        return new Answer(response.StatusCode, language, document.RootElement.Clone(), response.Headers.CacheControl?.ToString(), response.Headers.Location?.OriginalString, response.Headers.ETag?.ToString());
    }

    /// <summary>The real Api, started; <paramref name="configure"/> adjusts it after its composition (a fake provider, a setting).</summary>
    public static async Task<WebApplication> StartAsync(GeoDatabase database, ValkeyCache valkey, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Valkey"] = valkey.ConnectionString,
            ["ConnectionStrings:Geo"] = database.ConnectionString,
            ["Caching:InstanceName"] = database.Token,
            ["Http:RateLimit:PermitLimit"] = "1000",
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.AddFrappeApi();
        configure?.Invoke(builder);
        var app = builder.Build();
        app.MapFrappeApi();
        await app.StartAsync();
        return app;
    }
}
