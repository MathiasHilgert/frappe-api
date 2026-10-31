using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Frappe.Kernel.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
    IOptions<HttpJsonOptions> json,
    IHostApplicationLifetime lifetime,
    ILogger<CatalogueSnapshot<TQuery, TItem>> logger
)
    where TQuery : IQuery<TQuery, IReadOnlyList<TItem>>, new()
{
    private const string ContentType = "application/json; charset=utf-8";

    // How long a replica serves its stale snapshot after a failed refresh before one request tries again.
    private static readonly TimeSpan _retryAfter = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _maxAge = options.Value.CatalogueMaxAge;
    private readonly JsonSerializerOptions _json = json.Value.SerializerOptions;
    private readonly Lock _gate = new();
    private Snapshot? _snapshot;
    private Task<Snapshot>? _refresh;
    private long _failedAt;

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

    private async Task<Snapshot> CurrentAsync(CancellationToken cancellationToken)
    {
        Snapshot? snapshot = Volatile.Read(ref _snapshot);
        if (IsServable(snapshot))
        {
            return snapshot;
        }

        // Single flight: requests that find the snapshot cold or expired share one refresh instead of each reading
        // the catalogue. The refresh runs on the host's token, so a caller that leaves only stops its own wait.
        Task<Snapshot> refresh;
        lock (_gate)
        {
            snapshot = _snapshot;
            if (IsServable(snapshot))
            {
                return snapshot;
            }

            refresh = _refresh ??= Task.Run(RefreshAsync, CancellationToken.None);
        }

        return await refresh.WaitAsync(cancellationToken);
    }

    // Fresh, or stale while a failed refresh backs off.
    private bool IsServable([NotNullWhen(true)] Snapshot? snapshot) =>
        snapshot is not null
        && (
            !snapshot.IsExpired(_maxAge)
            || Stopwatch.GetElapsedTime(Interlocked.Read(ref _failedAt)) < _retryAfter
        );

    private async Task<Snapshot> RefreshAsync()
    {
        Snapshot? stale = Volatile.Read(ref _snapshot);
        try
        {
            Snapshot snapshot = await BuildAsync(lifetime.ApplicationStopping);
            Volatile.Write(ref _snapshot, snapshot);
            return snapshot;
        }
        // Only a database failure is an outage worth riding out on the stale snapshot; anything else, such as the
        // catalogue outgrowing its page cap, is a bug that must fail every request until it is fixed.
        catch (DbException exception) when (stale is not null)
        {
            Interlocked.Exchange(ref _failedAt, Stopwatch.GetTimestamp());
            Log.RefreshFailed(logger, typeof(TQuery).Name, exception);
            return stale;
        }
        finally
        {
            lock (_gate)
            {
                _refresh = null;
            }
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
