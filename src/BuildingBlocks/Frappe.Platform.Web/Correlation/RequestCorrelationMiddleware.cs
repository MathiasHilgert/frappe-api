using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.Web.Correlation;

/// <summary>Assigns every request an id, echoes it in <c>X-Request-Id</c> and flows it into logs and spans.</summary>
internal sealed class RequestCorrelationMiddleware(RequestDelegate next, TimeProvider timeProvider, ILogger<RequestCorrelationMiddleware> logger)
{
    public const string HeaderName = "X-Request-Id";
    private const int MaxLength = 128;

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

        using (logger.BeginScope(new Dictionary<string, object>(StringComparer.Ordinal) { ["RequestId"] = requestId }))
            await next(context);
    }

    private string ResolveId(string incoming) =>
        IsValid(incoming) ? incoming : Guid.CreateVersion7(timeProvider.GetUtcNow()).ToString();

    private static bool IsValid(string value)
    {
        if (value.Length is 0 or > MaxLength)
            return false;
        foreach (var c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
                return false;
        }
        return true;
    }
}
