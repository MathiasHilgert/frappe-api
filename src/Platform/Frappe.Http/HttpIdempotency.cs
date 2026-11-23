using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Frappe.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Frappe.Http;

/// <summary>
/// Retry-safe creations (HTTP-26, CQRS-10), opt-in per endpoint with <see cref="AcceptsIdempotencyKey{TBuilder}"/>. A
/// request that sends an <c>Idempotency-Key</c> runs once: its first successful answer is stored for
/// <see cref="IdempotencyKey.Retention"/> and replayed, with <c>Idempotent-Replayed: true</c>, to every later request
/// with the same key, endpoint, caller and content. Without the header the endpoint behaves as if it never opted in.
/// </summary>
public static partial class HttpIdempotency
{
    extension<TBuilder>(TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        /// <summary>
        /// Lets the endpoint's clients retry it safely with an <c>Idempotency-Key</c>. The endpoint's successful answers
        /// (<c>2xx</c>) are stored; a refusal or a failure is not, and releases the key, so the retry runs. Only for a
        /// creation whose answer is safe to repeat verbatim: never one that hands out a credential in a cookie or a header,
        /// since neither is stored.
        /// </summary>
        public TBuilder AcceptsIdempotencyKey()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.WithMetadata(new IdempotentMetadata());
            builder.ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
            return builder.AddEndpointFilter(RunAsync);
        }
    }

    /// <summary>The claim naming who is calling (<c>sub</c>, RFC 7519), which scopes a key to its caller.</summary>
    private const string SubjectClaim = "sub";

    /// <summary>The answer headers a replay repeats; anything else (a cookie, a date) belongs to the moment it was first written.</summary>
    private static readonly string[] ReplayedHeaders =
        [HeaderNames.Location, HeaderNames.ContentType, HeaderNames.ContentLanguage, HeaderNames.CacheControl, HeaderNames.ETag];

    private static async ValueTask<object?> RunAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var header = httpContext.Request.Headers[IdempotencyKey.HeaderName];
        if (StringValues.IsNullOrEmpty(header) && !httpContext.Request.Headers.ContainsKey(IdempotencyKey.HeaderName))
        {
            return await next(context);
        }

        if (header.Count != 1 || !IsValid(header[0]))
        {
            return ErrorsProblemHttpResult.Of(Error.InvalidValue(IdempotencyKey.InvalidCode, ("max", IdempotencyKey.MaxLength)));
        }

        var store = httpContext.RequestServices.GetRequiredService<IIdempotencyStore>();
        var key = ScopedKey(httpContext, header[0]!);
        var fingerprint = Fingerprint(context);
        var cancellationToken = httpContext.RequestAborted;

        IdempotencyClaim claim;
        try
        {
            claim = await store.ClaimAsync(key, fingerprint, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogStoreFailed(Log(httpContext), exception, "claim");
            return ErrorsProblemHttpResult.Of(new UnavailableError(IdempotencyKey.UnavailableCode,
                ("retryAfter", ErrorsProblemHttpResult.UnavailableRetryAfterSeconds)));
        }

        switch (claim.Outcome)
        {
            case IdempotencyOutcome.Replay:
                return new ReplayHttpResult(claim.Response!);
            case IdempotencyOutcome.InFlight:
                return ErrorsProblemHttpResult.Of(new ConflictError(IdempotencyKey.InProgressCode));
            case IdempotencyOutcome.Reused:
                return ErrorsProblemHttpResult.Of(new RuleViolationError(IdempotencyKey.ReusedCode));
        }

        StoredResponse stored;
        try
        {
            stored = await CaptureAsync(httpContext, await next(context));
        }
        catch
        {
            await ReleaseAsync(store, key, httpContext);
            throw;
        }

        if (stored is { Status: >= 200 and < 300 })
        {
            try
            {
                await store.CompleteAsync(key, fingerprint, stored, CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The request ran and the client has its answer: failing it now would invite the retry this exists to avoid.
                LogStoreFailed(Log(httpContext), exception, "complete");
            }
        }
        else
        {
            await ReleaseAsync(store, key, httpContext);
        }

        // Only now does the client get the body: the creation has committed and its answer is kept, so a write that fails
        // (the client left) cannot free the key and invite the retry that would create it again.
        if (stored.Body.Length > 0)
        {
            await httpContext.Response.Body.WriteAsync(stored.Body, httpContext.RequestAborted);
        }

        return new WrittenHttpResult();
    }

    /// <summary>
    /// Runs the endpoint's result into memory and keeps what a replay needs; the caller writes the body to the client. The
    /// result is executed here (not by the framework afterwards) so the answer to store is the one actually sent.
    /// </summary>
    private static async Task<StoredResponse> CaptureAsync(HttpContext httpContext, object? result)
    {
        var response = httpContext.Response;
        var original = response.Body;
        using var buffer = new MemoryStream();
        response.Body = buffer;
        try
        {
            if (result is IResult executable)
            {
                await executable.ExecuteAsync(httpContext);
            }
            else if (result is not null)
            {
                await Microsoft.AspNetCore.Http.Results.Ok(result).ExecuteAsync(httpContext);
            }
        }
        finally
        {
            response.Body = original;
        }

        var body = buffer.ToArray();
        var headers = ReplayedHeaders
            .Where(name => response.Headers.ContainsKey(name))
            .ToDictionary(name => name, name => response.Headers[name].OfType<string>().ToArray(), StringComparer.OrdinalIgnoreCase);
        return new StoredResponse(response.StatusCode, headers, body);
    }

    private static async Task ReleaseAsync(IIdempotencyStore store, string key, HttpContext httpContext)
    {
        try
        {
            await store.ReleaseAsync(key, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The claim expires on its own (IdempotencyKey.InFlightLifetime), so a failed release only delays the retry.
            LogStoreFailed(Log(httpContext), exception, "release");
        }
    }

    /// <summary>Printable ASCII without spaces, 1 to <see cref="IdempotencyKey.MaxLength"/> characters.</summary>
    private static bool IsValid(string? key) =>
        key is { Length: > 0 and <= IdempotencyKey.MaxLength } && key.All(c => c is > ' ' and < '\u007f');

    /// <summary>The key within its endpoint and caller, so one client's key never meets another's answer.</summary>
    private static string ScopedKey(HttpContext httpContext, string key)
    {
        var route = httpContext.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { } pattern } ? pattern : httpContext.Request.Path.Value;
        var caller = httpContext.User.FindFirst(SubjectClaim)?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.Identity?.Name
            ?? string.Empty;
        return Hash($"{httpContext.Request.Method}\n{route}\n{caller}\n{key}");
    }

    /// <summary>
    /// What makes two requests the same one: the route values, the query and the JSON body the endpoint binds. Only its
    /// digest is kept, so a password in the body is never stored.
    /// </summary>
    private static string Fingerprint(EndpointFilterInvocationContext context)
    {
        var httpContext = context.HttpContext;
        var bodyType = httpContext.GetEndpoint()?.Metadata.GetMetadata<IAcceptsMetadata>()?.RequestType;
        var body = bodyType is null ? null : context.Arguments.FirstOrDefault(argument =>
            argument is not null && bodyType.IsInstanceOfType(argument));
        var json = body is null ? string.Empty : JsonSerializer.Serialize(body, body.GetType(), ResultHttpExtensions.ApplicationJson(httpContext));
        var route = string.Join('&', httpContext.Request.RouteValues.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => $"{v.Key}={v.Value}"));
        return Hash($"{route}\n{httpContext.Request.QueryString.Value}\n{json}");
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static ILogger Log(HttpContext httpContext) =>
        httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(HttpIdempotency));

    [LoggerMessage(Level = LogLevel.Warning, Message = "The idempotency store failed to {Operation}")]
    private static partial void LogStoreFailed(ILogger logger, Exception exception, string operation);

    /// <summary>The stored answer, written again with <c>Idempotent-Replayed: true</c>.</summary>
    private sealed class ReplayHttpResult(StoredResponse stored) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var response = httpContext.Response;
            response.StatusCode = stored.Status;
            foreach (var (name, values) in stored.Headers)
            {
                response.Headers[name] = values;
            }

            response.Headers[IdempotencyKey.ReplayedHeaderName] = "true";
            if (stored.Body.Length > 0)
            {
                response.ContentLength = stored.Body.Length;
                await response.Body.WriteAsync(stored.Body, httpContext.RequestAborted);
            }
        }
    }

    /// <summary>The answer was already written to the client.</summary>
    private sealed class WrittenHttpResult : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext) => Task.CompletedTask;
    }
}
