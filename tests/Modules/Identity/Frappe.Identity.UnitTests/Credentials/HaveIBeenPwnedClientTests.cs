using System.Net;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Domain.Credentials;
using Frappe.Identity.Infrastructure.Credentials;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Identity.UnitTests.Credentials;

/// <summary>
/// The breach client as the module composes it, over a fake transport: k-anonymity, padding, and an answer of unknown (never an exception)
/// whenever the list is slow, down or refuses (S4, CFG-08).
/// </summary>
public sealed class HaveIBeenPwnedClientTests
{
    // SHA-1 of "correct horse battery": prefix and suffix as the range API spells them.
    private const string Prefix = "98DEC";
    private const string Suffix = "C62ECE399A22ED30D490EF333BE7FDE7385";

    private static readonly Password Listed = Password.Create("correct horse battery").Value;

    private static ServiceProvider Compose(StubTransport transport, Dictionary<string, string?>? settings = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build());
        services.AddPasswords();
        services.AddHttpClient<IBreachedPasswords, HaveIBeenPwnedClient>().ConfigurePrimaryHttpMessageHandler(() => transport);
        return services.BuildServiceProvider();
    }

    private static Task<bool?> Ask(ServiceProvider provider, CancellationToken cancellationToken = default) =>
        provider.GetRequiredService<IBreachedPasswords>().IsBreachedAsync(Listed, cancellationToken);

    [Test]
    public async Task A_suffix_the_list_returns_with_a_count_is_a_hit_and_only_the_prefix_is_sent_with_padding()
    {
        // Arrange
        using var transport = new StubTransport((_, _) => Answer(HttpStatusCode.OK, $"0018A45C4D1DEF81644B54AB7F969B88D65:3\r\n{Suffix}:42\r\n"));
        await using var provider = Compose(transport);

        // Act
        var breached = await Ask(provider);

        // Assert
        var request = transport.Requests.Single();
        using (Assert.Multiple())
        {
            await Assert.That(breached).IsTrue();
            await Assert.That(request.RequestUri!.AbsoluteUri).IsEqualTo($"https://api.pwnedpasswords.com/range/{Prefix}");
            await Assert.That(request.Method).IsEqualTo(HttpMethod.Get);
            await Assert.That(request.Headers.GetValues("Add-Padding").Single()).IsEqualTo("true");
            await Assert.That(request.RequestUri.AbsoluteUri).DoesNotContain(Suffix);
        }
    }

    [Test]
    public async Task A_list_without_the_suffix_is_a_miss()
    {
        // Arrange
        using var transport = new StubTransport(
            (_, _) => Answer(HttpStatusCode.OK, "0018A45C4D1DEF81644B54AB7F969B88D65:3\n00D4F6E8FA6EECAD2A3AA415EEC418D38EC:2\n"));
        await using var provider = Compose(transport);

        // Act
        var breached = await Ask(provider);

        // Assert
        await Assert.That(breached).IsFalse();
    }

    [Test]
    public async Task A_padded_line_with_a_zero_count_is_not_a_hit_even_when_its_suffix_matches()
    {
        // Arrange
        using var transport = new StubTransport((_, _) => Answer(HttpStatusCode.OK, $"{Suffix.ToLowerInvariant()}:0\r\n"));
        await using var provider = Compose(transport);

        // Act
        var breached = await Ask(provider);

        // Assert
        await Assert.That(breached).IsFalse();
    }

    [Test]
    public async Task A_suffix_in_lower_case_still_matches()
    {
        // Arrange
        using var transport = new StubTransport((_, _) => Answer(HttpStatusCode.OK, $"{Suffix.ToLowerInvariant()}:7\r\n"));
        await using var provider = Compose(transport);

        // Act
        var breached = await Ask(provider);

        // Assert
        await Assert.That(breached).IsTrue();
    }

    [Test]
    public async Task A_list_that_does_not_answer_within_the_timeout_gives_unknown_and_is_not_retried()
    {
        // Arrange
        using var transport = new StubTransport(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Reply(HttpStatusCode.OK, "");
        });
        await using var provider = Compose(transport, new() { ["Identity:BreachCheck:Timeout"] = "00:00:00.100" });

        // Act
        var breached = await Ask(provider);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(breached).IsNull();
            await Assert.That(transport.Requests.Count).IsEqualTo(1);
        }
    }

    [Test]
    [Arguments(HttpStatusCode.InternalServerError)]
    [Arguments(HttpStatusCode.TooManyRequests)]
    [Arguments(HttpStatusCode.Forbidden)]
    public async Task A_refusal_or_a_server_error_gives_unknown_without_a_retry(HttpStatusCode status)
    {
        // Arrange
        using var transport = new StubTransport((_, _) => Answer(status, "nope"));
        await using var provider = Compose(transport);

        // Act
        var breached = await Ask(provider);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(breached).IsNull();
            await Assert.That(transport.Requests.Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task A_broken_connection_gives_unknown()
    {
        // Arrange
        using var transport = new StubTransport((_, _) => throw new HttpRequestException("connection refused"));
        await using var provider = Compose(transport);

        // Act
        var breached = await Ask(provider);

        // Assert
        await Assert.That(breached).IsNull();
    }

    [Test]
    public async Task A_list_that_keeps_failing_opens_the_breaker_and_the_next_call_never_leaves_the_process()
    {
        // Arrange
        using var transport = new StubTransport((_, _) => Answer(HttpStatusCode.ServiceUnavailable, ""));
        await using var provider = Compose(transport, new() { ["Identity:BreachCheck:BreakDuration"] = "00:01:00" });
        var answers = new List<bool?>();

        // Act
        for (var call = 0; call < 8; call++)
        {
            answers.Add(await Ask(provider));
        }

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(answers.All(answer => answer is null)).IsTrue();
            await Assert.That(transport.Requests.Count).IsEqualTo(5);
        }
    }

    [Test]
    public async Task The_callers_own_cancellation_is_not_swallowed()
    {
        // Arrange
        using var transport = new StubTransport(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Reply(HttpStatusCode.OK, "");
        });
        await using var provider = Compose(transport, new() { ["Identity:BreachCheck:Timeout"] = "00:00:10" });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act
        var asking = Ask(provider, cancellation.Token);

        // Assert
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await asking);
    }

    [Test]
    public async Task A_configured_base_address_is_the_one_asked_with_or_without_a_trailing_slash()
    {
        // Arrange
        using var transport = new StubTransport((_, _) => Answer(HttpStatusCode.OK, ""));
        await using var provider = Compose(transport, new() { ["Identity:BreachCheck:BaseUrl"] = "http://mirror.internal:8080/pwned" });

        // Act
        await Ask(provider);

        // Assert
        await Assert.That(transport.Requests.Single().RequestUri!.AbsoluteUri).IsEqualTo($"http://mirror.internal:8080/pwned/range/{Prefix}");
    }

    [Test]
    public async Task A_body_that_stalls_after_the_headers_gives_unknown_within_the_timeout()
    {
        // Arrange
        using var transport = new StubTransport(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) }));
        await using var provider = Compose(transport, new() { ["Identity:BreachCheck:Timeout"] = "00:00:00.200" });
        var clock = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var breached = await Ask(provider);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(breached).IsNull();
            await Assert.That(clock.Elapsed).IsLessThan(TimeSpan.FromSeconds(10));
        }
    }

    [Test]
    public async Task The_request_shows_the_prefix_redacted_in_traces()
    {
        // Arrange
        using var transport = new StubTransport((_, _) => Answer(HttpStatusCode.OK, ""));
        await using var provider = Compose(transport);

        // Act
        await Ask(provider);

        // Assert
        var shown = Frappe.Observability.OutgoingRequestTelemetry.TelemetryUrlOf(transport.Requests.Single());
        using (Assert.Multiple())
        {
            await Assert.That(shown).IsEqualTo("https://api.pwnedpasswords.com/range/REDACTED");
            await Assert.That(shown).DoesNotContain(Prefix);
        }
    }

    /// <summary>A body that never delivers a byte and only ends when its reader is cancelled.</summary>
    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };

    private static Task<HttpResponseMessage> Answer(HttpStatusCode status, string body) => Task.FromResult(Reply(status, body));

    /// <summary>A transport that answers from a function and keeps every request it received.</summary>
    private sealed class StubTransport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        private readonly object _gate = new();

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Requests.Add(request);
            }

            return answer(request, cancellationToken);
        }
    }
}
