using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Frappe.Identity.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Frappe.Identity.Tests.Security;

public sealed class HibpBreachedPasswordCheckerTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private readonly ServiceProvider _metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();

    [Fact]
    public async Task IsBreachedAsync_SuffixListedWithCount_ReturnsTrue()
    {
        var handler = new FakeHandler(_ => Ok($"0000000000000000000000000000000000A:3\r\n{Suffix(Password)}:42\r\n"));

        (await Checker(handler).IsBreachedAsync(Password, TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task IsBreachedAsync_SendsOnlyThePrefixWithPadding()
    {
        var handler = new FakeHandler(_ => Ok(""));

        await Checker(handler).IsBreachedAsync(Password, TestContext.Current.CancellationToken);

        handler.Request!.RequestUri!.AbsolutePath.ShouldBe($"/range/{Hash(Password)[..5]}");
        handler.Request.Headers.GetValues("Add-Padding").ShouldBe(["true"]);
    }

    [Fact]
    public async Task IsBreachedAsync_SuffixMissing_ReturnsFalse()
    {
        var handler = new FakeHandler(_ => Ok("0000000000000000000000000000000000A:3\r\n"));

        (await Checker(handler).IsBreachedAsync(Password, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task IsBreachedAsync_PaddingEntryWithZeroCount_IsIgnored()
    {
        var handler = new FakeHandler(_ => Ok($"{Suffix(Password)}:0\r\n"));

        (await Checker(handler).IsBreachedAsync(Password, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task IsBreachedAsync_ServerError_FailsOpenAndCounts()
    {
        using var counted = new CounterListener(IdentityMetrics.BreachCheckUnavailable);
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        (await Checker(handler).IsBreachedAsync(Password, TestContext.Current.CancellationToken)).ShouldBeFalse();
        counted.Total.ShouldBe(1);
    }

    [Fact]
    public async Task IsBreachedAsync_SlowServer_FailsOpenAfterTimeout()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Ok("");
        });

        var started = TimeProvider.System.GetTimestamp();
        (await Checker(handler).IsBreachedAsync(Password, TestContext.Current.CancellationToken)).ShouldBeFalse();

        TimeProvider.System.GetElapsedTime(started).ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task IsBreachedAsync_CallerCancels_Throws()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Ok("");
        });
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(Checker(handler).IsBreachedAsync(Password, cancelled.Token));
    }

    public void Dispose() => _metrics.Dispose();

    private HibpBreachedPasswordChecker Checker(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://hibp.test/") },
            Options.Create(new BreachedPasswordOptions { TimeoutMilliseconds = 200 }),
            new IdentityMetrics(_metrics.GetRequiredService<IMeterFactory>()),
            NullLogger<HibpBreachedPasswordChecker>.Instance);

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

#pragma warning disable CA5350 // HIBP keys its range API by SHA-1; the test mirrors it.
    private static string Hash(string password) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
#pragma warning restore CA5350

    private static string Suffix(string password) => Hash(password)[5..];

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this((request, _) => Task.FromResult(respond(request))) { }

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return respond(request, cancellationToken);
        }
    }
}
