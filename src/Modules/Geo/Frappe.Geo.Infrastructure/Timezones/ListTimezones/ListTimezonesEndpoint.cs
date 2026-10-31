using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Frappe.Geo.Application.Timezones.ListTimezones;
using Frappe.Kernel.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Frappe.Geo.Infrastructure.Timezones.ListTimezones;

/// <summary><c>GET /v1/geo/timezones</c>: every IANA time zone, anonymous and publicly cacheable.</summary>
internal static class ListTimezonesEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group
            .MapGet("/timezones", HandleAsync)
            .WithName("ListTimezones")
            .WithSummary("Lists every IANA time zone, ordered by id.")
            .AllowAnonymous();

    private static async Task<
        Results<Ok<ListTimezonesResponse>, StatusCodeHttpResult, ProblemHttpResult>
    > HandleAsync(
        HttpContext context,
        IDispatcher dispatcher,
        IOptions<GeoOptions> options,
        CancellationToken cancellationToken
    )
    {
        ErrorOr<IReadOnlyList<TimezoneDto>> result = await dispatcher.QueryAsync(
            new ListTimezonesQuery(),
            cancellationToken
        );
        // shortcut: the query has no expected error and Platform has no ErrorOr-to-Problem mapper yet; map through
        // it once it exists.
        if (result.IsError)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError);
        }

        EntityTagHeaderValue etag = ETagOf(result.Value);
        SetCacheHeaders(context, etag, options.Value);
        if (
            context
                .Request.GetTypedHeaders()
                .IfNoneMatch.Any(tag =>
                    tag.Equals(EntityTagHeaderValue.Any)
                    || tag.Compare(etag, useStrongComparison: false)
                )
        )
        {
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        return TypedResults.Ok(new ListTimezonesResponse(result.Value, NextCursor: null));
    }

    // Only a 200 or a 304 is cacheable: an explicit max-age would let shared caches keep an error for a day.
    // No principal and no locale shape the body, so every cache may share it and no Vary is needed.
    private static void SetCacheHeaders(
        HttpContext context,
        EntityTagHeaderValue etag,
        GeoOptions options
    )
    {
        ResponseHeaders headers = context.Response.GetTypedHeaders();
        headers.ETag = etag;
        headers.CacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = options.TimezonesMaxAge,
        };
    }

    // A hash of the rows served, not the host's TZDB version: mid-deploy a host may serve rows another release
    // seeded. Hashing the rows, not the JSON, avoids serializing twice; ids are never empty and never hold a newline.
    private static EntityTagHeaderValue ETagOf(IReadOnlyList<TimezoneDto> zones)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ReadOnlySpan<byte> separator = MemoryMarshal.AsBytes("\n".AsSpan());
        foreach (TimezoneDto zone in zones)
        {
            hash.AppendData(MemoryMarshal.AsBytes(zone.Id.AsSpan()));
            hash.AppendData(separator);
            hash.AppendData(MemoryMarshal.AsBytes(zone.CanonicalId.AsSpan()));
            hash.AppendData(separator);
        }

        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        hash.GetHashAndReset(digest);
        return new EntityTagHeaderValue($"\"{Convert.ToHexStringLower(digest[..16])}\"");
    }
}
