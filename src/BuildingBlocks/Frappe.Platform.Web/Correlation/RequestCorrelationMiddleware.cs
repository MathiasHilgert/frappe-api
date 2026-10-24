using System.Buffers;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.Web.Correlation;

/// <summary>Assigns every request an id, echoes it in <c>X-Request-Id</c> and flows it into logs and spans.</summary>
internal sealed class RequestCorrelationMiddleware(RequestDelegate next, TimeProvider timeProvider, ILogger<RequestCorrelationMiddleware> logger)
{
    public const string HeaderName = "X-Request-Id";
    private const int MaxLength = 128;

    private static readonly SearchValues<char> Allowed =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789._-");

    // A cached scope template: RequestId flows into every log line as a structured property, with no dictionary per request.
    private static readonly Func<ILogger, string, IDisposable?> BeginRequestScope =
        LoggerMessage.DefineScope<string>("RequestId:{RequestId}");

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = ResolveId(context.Request.Headers[HeaderName].ToString());
        context.TraceIdentifier = requestId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = requestId;
            return Task.CompletedTask;
        });
        Activity.Current?.SetTag("http.request.id", requestId);

        using (BeginRequestScope(logger, requestId))
            await next(context);
    }

    private string ResolveId(string incoming) =>
        IsValid(incoming) ? incoming : Guid.CreateVersion7(timeProvider.GetUtcNow()).ToString();

    private static bool IsValid(string value) =>
        value.Length is > 0 and <= MaxLength && !value.AsSpan().ContainsAnyExcept(Allowed);
}
