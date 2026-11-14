using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Frappe.Http;

/// <summary>Marks an endpoint whose successful responses may be cached; documentation reads it to declare the headers.</summary>
public sealed record CacheableMetadata(TimeSpan MaxAge);

/// <summary>Marks an endpoint whose successful responses no cache may keep; documentation reads it to declare the header.</summary>
#pragma warning disable S2094 // Endpoint metadata is looked up by type; the marker carries no data on purpose.
public sealed class NoStoreMetadata;
#pragma warning restore S2094

/// <summary>
/// HTTP caching (RFC 9111). Every endpoint declares its lifetime: <see cref="CacheFor{TBuilder}"/> for read-only data,
/// whose successful response is <c>public</c> for a <c>max-age</c>, carries a strong <c>ETag</c> computed from its
/// representation, and answers <c>304 Not Modified</c> without a body to a matching <c>If-None-Match</c> (RFC 9110
/// §13.1.2); or <see cref="NoStore{TBuilder}"/> for answers no cache may keep. Errors are never touched, so they keep
/// their own caching rules.
/// </summary>
public static class HttpCaching
{
    private const string MaxAgeLimitKey = "frappe.cache-max-age-limit";

    /// <summary>
    /// Caches the endpoint's <c>200</c> responses for <paramref name="maxAge"/>. The response must be the JSON value of
    /// an <see cref="Ok{TValue}"/> (alone or in a <see cref="Results{TResult1, TResult2}"/>); anything else passes
    /// through unchanged.
    /// </summary>
    public static TBuilder CacheFor<TBuilder>(this TBuilder builder, TimeSpan maxAge)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAge, TimeSpan.Zero);

        builder.WithMetadata(new CacheableMetadata(maxAge));
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);
            var httpContext = context.HttpContext;
            var innermost = result is INestedHttpResult nested ? nested.Result : result;
            if (innermost is IValueHttpResult { Value: { } value } and IStatusCodeHttpResult { StatusCode: StatusCodes.Status200OK })
            {
                var limit = httpContext.Items.TryGetValue(MaxAgeLimitKey, out var item) && item is TimeSpan shorter ? shorter : maxAge;
                return new CacheableJsonHttpResult(value, limit < maxAge ? limit : maxAge);
            }

            return result;
        });
    }

    /// <summary>
    /// Forbids any cache to keep the endpoint's responses (<c>Cache-Control: no-store</c>, RFC 9111 §5.2.2.5) and sends
    /// no validator: for answers that are personal or change with every call (address suggestions, administration
    /// reads that must show the latest state). Problems already carry <c>no-store</c> on their own.
    /// </summary>
    public static TBuilder NoStore<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new NoStoreMetadata());
        return builder.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });
    }

    /// <summary>
    /// Shortens this response's <c>max-age</c> when its content expires sooner than the endpoint's (a value that depends
    /// on the current time, for example). The shortest limit wins; a negative one counts as zero.
    /// </summary>
    public static void LimitMaxAge(HttpContext httpContext, TimeSpan maxAge)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var limit = maxAge < TimeSpan.Zero ? TimeSpan.Zero : maxAge;
        if (httpContext.Items.TryGetValue(MaxAgeLimitKey, out var item) && item is TimeSpan existing && existing <= limit)
        {
            return;
        }

        httpContext.Items[MaxAgeLimitKey] = limit;
    }

    /// <summary>
    /// Writes the value as JSON with its validators. The ETag hashes the bytes sent and the languages they are written
    /// in, so two language variants never share one (RFC 9110 §8.8.3: an entity tag identifies one representation).
    /// </summary>
    private sealed class CacheableJsonHttpResult(object value, TimeSpan maxAge) : IResult
    {
        /// <summary>Bytes of SHA-256 kept in the tag: collisions stay negligible for a validator.</summary>
        private const int TagBytes = 16;

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), ResultHttpExtensions.ApplicationJson(httpContext));
            var response = httpContext.Response;
            var etag = new EntityTagHeaderValue(Tag(response.Headers.ContentLanguage, body));

            response.Headers.ETag = etag.ToString();
            response.Headers.CacheControl = "public, max-age=" + ((long)maxAge.TotalSeconds).ToString(CultureInfo.InvariantCulture);

            if (Matches(httpContext.Request.Headers.IfNoneMatch, etag))
            {
                // Only the fields a 304 must repeat (RFC 9110 §15.4.5): ETag, Cache-Control and Vary; the stored
                // representation keeps its own Content-Language.
                response.StatusCode = StatusCodes.Status304NotModified;
                response.Headers.ContentLanguage = StringValues.Empty;
                return;
            }

            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength = body.Length;
            await response.Body.WriteAsync(body, httpContext.RequestAborted);
        }

        private static string Tag(StringValues contentLanguage, byte[] body)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(Encoding.UTF8.GetBytes(contentLanguage.ToString() + "\n"));
            hash.AppendData(body);
            return "\"" + Base64Url.EncodeToString(hash.GetHashAndReset().AsSpan(0, TagBytes)) + "\"";
        }

        /// <summary>RFC 9110 §13.1.2: <c>*</c> matches any representation, and tags are compared weakly.</summary>
        private static bool Matches(StringValues ifNoneMatch, EntityTagHeaderValue etag) =>
            !StringValues.IsNullOrEmpty(ifNoneMatch)
            && EntityTagHeaderValue.TryParseList(ifNoneMatch, out var tags)
            && tags.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(etag, useStrongComparison: false));
    }
}
