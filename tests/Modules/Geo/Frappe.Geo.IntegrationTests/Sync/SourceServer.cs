using System.Net;
using System.Net.Http.Headers;

namespace Frappe.Geo.IntegrationTests.Sync;

/// <summary>
/// A source as the sync sees it, in process: serves one fixture folder's files (Georef's by default) with an ETag per content, answers 304 to a matching
/// If-None-Match, and lets a test unpublish a file or replace its content. A file in a subfolder is named by its relative path
/// (<c>2026/provincias/caba.json</c>) and answers the requests whose path ends with it.
/// </summary>
internal sealed class SourceServer : HttpMessageHandler
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    public SourceServer(string fixtures = "georef")
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtures);
        foreach (var path in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories))
        {
            _files[Path.GetRelativePath(folder, path).Replace(Path.DirectorySeparatorChar, '/')] = File.ReadAllText(path);
        }
    }

    public List<(string File, HttpStatusCode Status)> Requests { get; } = [];

    public void Replace(string file, Func<string, string> edit) => _files[file] = edit(_files[file]);

    public void Unpublish(string file) => _files.Remove(file);

    public void Add(string file, string content) => _files[file] = content;

    public string Content(string file) => _files[file];

    public bool Serves(string file) => _files.ContainsKey(file);

    /// <summary>How long the server waits before answering, to drive the resilience pipeline's timeout.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>When set, this file's body breaks after its first bytes, as a dropped connection does.</summary>
    public string? CutBodyOf { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(Delay, cancellationToken);
        var file = FileOf(request.RequestUri!.AbsolutePath);
        var response = Answer(file, request);
        if (file == CutBodyOf && response.StatusCode == HttpStatusCode.OK)
        {
            response.Content = new CutContent();
        }

        Requests.Add((file, response.StatusCode));
        return response;
    }

    /// <summary>The served file a path asks for: the longest name it ends with, else its last segment.</summary>
    private string FileOf(string path) =>
        _files.Keys.Where(name => path.EndsWith("/" + name, StringComparison.Ordinal)).MaxBy(name => name.Length) ?? Path.GetFileName(path);

    private HttpResponseMessage Answer(string file, HttpRequestMessage request)
    {
        if (!_files.TryGetValue(file, out var content))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var etag = new EntityTagHeaderValue("\"" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)))[..16] + "\"");
        if (request.Headers.IfNoneMatch.Any(tag => tag.Tag == etag.Tag))
        {
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) };
        response.Headers.ETag = etag;
        return response;
    }

    /// <summary>
    /// A body whose stream breaks after its first bytes, as a dropped connection does with headers already read: the read
    /// itself throws <see cref="HttpIOException"/>, not the request.
    /// </summary>
    private sealed class CutContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            throw new NotSupportedException("Read it as a stream.");

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new CutStream());

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }

    private sealed class CutStream : MemoryStream
    {
        private bool _started;

        public CutStream()
            : base("{\"local"u8.ToArray())
        {
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_started)
            {
                throw new HttpIOException(HttpRequestError.ResponseEnded, "The connection was closed mid-body.");
            }

            _started = true;
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}
