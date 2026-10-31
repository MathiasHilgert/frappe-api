using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Frappe.Kernel.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Frappe.Geo.Infrastructure.Catalogues;

/// <summary>
/// Serves a public catalogue from an in-memory snapshot: the JSON body <typeparamref name="TQuery"/> lists and a
/// strong <c>ETag</c> hashed from those bytes, built once and rebuilt after <see cref="GeoOptions.CatalogueMaxAge"/>.
/// </summary>
/// <typeparam name="TQuery">The query that lists the whole catalogue.</typeparam>
/// <typeparam name="TItem">The catalogue's item.</typeparam>
// shortcut: each replica holds its own snapshot, stale by up to the max-age after the seed changes rows; move it
// onto the Kernel ICache port, with its jitter and tag invalidation, once that port exists.
internal sealed class CatalogueSnapshot<TQuery, TItem>(
    IServiceScopeFactory scopes,
    IOptions<GeoOptions> options,
    IOptions<HttpJsonOptions> json
) : IDisposable
    where TQuery : IQuery<TQuery, IReadOnlyList<TItem>>, new()
{
    private const string ContentType = "application/json; charset=utf-8";

    private readonly TimeSpan _maxAge = options.Value.CatalogueMaxAge;
    private readonly JsonSerializerOptions _json = json.Value.SerializerOptions;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private Snapshot? _snapshot;

    /// <summary>Answers a GET with the body, or with 304 when <c>If-None-Match</c> names the snapshot's tag.</summary>
    /// <param name="context">The request.</param>
    /// <returns>The body, or 304 with no body.</returns>
    public async Task<Results<FileContentHttpResult, StatusCodeHttpResult>> ServeAsync(
        HttpContext context
    )
    {
        Snapshot snapshot = await CurrentAsync(context.RequestAborted);
        // Set only once the snapshot exists: a failed read throws first, so an error is never publicly cached.
        // No principal and no locale shape the body, so every cache may share it and no Vary is needed.
        ResponseHeaders headers = context.Response.GetTypedHeaders();
        headers.ETag = snapshot.ETag;
        headers.CacheControl = new CacheControlHeaderValue { Public = true, MaxAge = _maxAge };
        bool matches = context
            .Request.GetTypedHeaders()
            .IfNoneMatch.Any(tag =>
                tag.Equals(EntityTagHeaderValue.Any)
                || tag.Compare(snapshot.ETag, useStrongComparison: false)
            );
        return matches
            ? TypedResults.StatusCode(StatusCodes.Status304NotModified)
            : TypedResults.Bytes(snapshot.Body, ContentType);
    }

    /// <inheritdoc />
    public void Dispose() => _refresh.Dispose();

    private async ValueTask<Snapshot> CurrentAsync(CancellationToken cancellationToken)
    {
        Snapshot? snapshot = Volatile.Read(ref _snapshot);
        if (snapshot is not null && !snapshot.IsExpired(_maxAge))
        {
            return snapshot;
        }

        // Single flight: requests that find the snapshot cold or expired wait for one refresh instead of each
        // reading the catalogue.
        await _refresh.WaitAsync(cancellationToken);
        try
        {
            snapshot = _snapshot;
            if (snapshot is not null && !snapshot.IsExpired(_maxAge))
            {
                return snapshot;
            }

            snapshot = await BuildAsync(cancellationToken);
            Volatile.Write(ref _snapshot, snapshot);
            return snapshot;
        }
        finally
        {
            _refresh.Release();
        }
    }

    // The snapshot outlives every request, so the query runs in a scope of its own; it goes through the dispatcher
    // as any request would, so its policy and transaction apply.
    private async Task<Snapshot> BuildAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        ErrorOr<IReadOnlyList<TItem>> items = await scope
            .ServiceProvider.GetRequiredService<IDispatcher>()
            .QueryAsync(new TQuery(), cancellationToken);
        // A catalogue query has no expected failure, so an error here is a bug, not a response.
        if (items.IsError)
        {
            throw new InvalidOperationException(
                $"{typeof(TQuery).Name} failed: {items.FirstError.Code}."
            );
        }

        // The options the endpoint's JSON results use, so the bytes are what a typed result would write.
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(
            new CataloguePage<TItem>(items.Value, NextCursor: null),
            _json
        );
        string tag = Convert.ToHexStringLower(SHA256.HashData(body).AsSpan(0, 16));
        return new Snapshot(body, new EntityTagHeaderValue($"\"{tag}\""), Stopwatch.GetTimestamp());
    }

    // The age is monotonic elapsed time, not a wall-clock instant, so no clock is needed.
    private sealed record Snapshot(byte[] Body, EntityTagHeaderValue ETag, long BuiltAt)
    {
        public bool IsExpired(TimeSpan maxAge) => Stopwatch.GetElapsedTime(BuiltAt) >= maxAge;
    }
}
