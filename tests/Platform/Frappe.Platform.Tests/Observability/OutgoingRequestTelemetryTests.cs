using System.Diagnostics;
using Frappe.Observability;
using Frappe.Observability.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Trace;

namespace Frappe.Platform.Tests.Observability;

/// <summary>
/// A request marked with <see cref="OutgoingRequestTelemetry.ShowInTelemetryAs"/> exports its redacted URL, through the
/// real HttpClient instrumentation and a real socket: a fake primary handler would emit no client span at all.
/// </summary>
public sealed class OutgoingRequestTelemetryTests
{
    private const string Secret = "secret-place-7f3a";

    private static async Task<WebApplication> StubAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapGet("/v1/places/{id}", () => Microsoft.AspNetCore.Http.Results.Ok());
        await app.StartAsync();
        return app;
    }

    private static Uri BaseOf(WebApplication app) =>
        new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());

    private static IHost Host(List<Activity> spans)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.AddFrappeTelemetry("frappe-test", traces => traces.AddInMemoryExporter(spans));
        builder.Services.Configure<HttpClientTraceInstrumentationOptions>(options => options.EnrichWithHttpRequestMessage = OutgoingRequestTelemetry.Redact);
        builder.Services.AddHttpClient("stub");
        return builder.Build();
    }

    [Test]
    public async Task A_marked_request_exports_its_redacted_url_and_an_unmarked_one_its_own()
    {
        await using var stub = await StubAsync();
        var root = BaseOf(stub);
        List<Activity> spans = [];
        using var host = Host(spans);
        await host.StartAsync();
        var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("stub");

        using (var marked = new HttpRequestMessage(HttpMethod.Get, new Uri(root, $"/v1/places/{Secret}?languageCode=es")))
        {
            marked.ShowInTelemetryAs(new Uri(root, $"/v1/places/{OutgoingRequestTelemetry.Redacted}").AbsoluteUri);
            using var answered = await client.SendAsync(marked);
        }

        using var unmarked = await client.GetAsync(new Uri(root, "/v1/places/public"));

        host.Services.GetRequiredService<TracerProvider>().ForceFlush();
        await host.StopAsync();

        // HttpClient spans are process-wide: other tests' requests reach this provider too, so stop it exporting
        // (disposing the host disposes the provider) before reading the list, and keep the stub's spans only.
        host.Dispose();
        var urls = spans.Where(s => s.Kind == ActivityKind.Client && Equals(s.GetTagItem("server.port"), root.Port))
            .Select(s => s.GetTagItem(OutgoingRequestTelemetry.UrlFullTag) as string ?? string.Empty).ToList();
        await Assert.That(urls).IsEquivalentTo([new Uri(root, "/v1/places/REDACTED").AbsoluteUri, new Uri(root, "/v1/places/public").AbsoluteUri]);
        var everything = string.Join("\n", spans.SelectMany(s => s.TagObjects.Select(t => $"{s.DisplayName} {t.Key}={t.Value}")));
        await Assert.That(everything).DoesNotContain(Secret);
    }
}
