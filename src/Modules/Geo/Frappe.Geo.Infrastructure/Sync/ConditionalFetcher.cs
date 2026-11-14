using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Frappe.Geo.Infrastructure.Sync;

/// <summary>
/// Downloads a source's file with the validators of the last success (<c>If-None-Match</c>, <c>If-Modified-Since</c>),
/// so an unchanged file costs a 304, and bounds the body in size (<see cref="MaxBytes"/>) and in time
/// (<see cref="BodyTimeout"/>: the client's and the resilience pipeline's timeouts stop at the headers). Any status other
/// than 200, 304 or 404 is a failed attempt (<see cref="HttpRequestException"/>).
/// </summary>
internal sealed class ConditionalFetcher(HttpClient http, TimeSpan? bodyTimeout = null)
{
    /// <summary>The longest a body may take to arrive once its headers did: 2 minutes by default.</summary>
    public TimeSpan BodyTimeout { get; } = bodyTimeout ?? TimeSpan.FromMinutes(2);

    /// <summary>The largest file a source may publish: 64 MB, far above any reference dataset Geo reads today.</summary>
    public const long MaxBytes = 64 * 1024 * 1024;

    public async Task<FetchResult> FetchAsync(Uri location, string? etag, DateTimeOffset? lastModified, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, location);
        if (etag is not null && EntityTagHeaderValue.TryParse(etag, out var tag))
        {
            request.Headers.IfNoneMatch.Add(tag);
        }

        if (lastModified is not null)
        {
            request.Headers.IfModifiedSince = lastModified;
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotModified:
                return new FetchResult(FetchStatus.NotModified, ETag: etag, LastModified: lastModified);
            case HttpStatusCode.NotFound:
                return new FetchResult(FetchStatus.NotPublished);
            case HttpStatusCode.OK:
                break;
            default:
                throw new HttpRequestException($"{location} answered {(int)response.StatusCode}.", null, response.StatusCode);
        }

        if (response.Content.Headers.ContentLength > MaxBytes)
        {
            throw new HttpRequestException($"{location} is larger than {MaxBytes} bytes.");
        }

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(BodyTimeout);
        await using var stream = await response.Content.ReadAsStreamAsync(bounded.Token);
        using var buffer = new MemoryStream();
        await CopyBoundedAsync(stream, buffer, location, bounded.Token);
        var body = buffer.ToArray();
        return new FetchResult(
            FetchStatus.Content,
            body,
            response.Headers.ETag?.ToString(),
            response.Content.Headers.LastModified,
            Convert.ToHexStringLower(SHA256.HashData(body)));
    }

    private static async Task CopyBoundedAsync(Stream source, MemoryStream target, Uri location, CancellationToken cancellationToken)
    {
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (target.Length + read > MaxBytes)
            {
                throw new HttpRequestException($"{location} is larger than {MaxBytes} bytes.");
            }

            await target.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
    }
}
