using System.Net;
using System.Text;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>An <see cref="HttpMessageHandler"/> that answers from a script and keeps every request it received, body included.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _script = new();
    private readonly object _gate = new();
    private int _hangs;

    public List<(HttpRequestMessage Request, string Body, DateTimeOffset At)> Received { get; } = [];

    public StubHandler Respond(HttpStatusCode status, string body = "{}", params (string Name, string Value)[] headers)
    {
        _script.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        });
        return this;
    }

    /// <summary>Answers nothing until the caller gives up, as a provider that does not reply.</summary>
    public StubHandler Hang()
    {
        _hangs++;
        return this;
    }

    public StubHandler Throw(Exception exception)
    {
        _script.Enqueue(() => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Func<HttpResponseMessage>? next = null;
        bool hang;
        lock (_gate)
        {
            Received.Add((request, body, DateTimeOffset.UtcNow));
            hang = _script.Count == 0 && _hangs > 0;
            if (_script.Count > 0)
            {
                next = _script.Dequeue();
            }
        }

        if (hang)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        return next?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.InternalServerError);
    }
}
