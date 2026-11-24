using System.Diagnostics.Metrics;
using Frappe.Caching;
using Frappe.Identity.Application.Codes;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Limits;
using Frappe.Identity.Limits.Valkey;
using Frappe.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Identity.IntegrationTests;

/// <summary>
/// The attempt limiter on a real Valkey: five guesses per code and no more, however many run at once, shared by every replica; a guess limit
/// that cannot count refuses, a send limit lets the call through (S5, SEC-14).
/// </summary>
[ClassDataSource<ValkeyServer>(Shared = SharedType.PerTestSession)]
public sealed class ValkeyAttemptLimiterTests(ValkeyServer server)
{
    private static readonly LimitFlow Tiny = new("test.tiny", 2, TimeSpan.FromMinutes(5), LimitFailure.FailClosed);

    private static ServiceProvider Build(string connectionString, string instance, Dictionary<string, string?>? settings = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build());
        services.AddFrappeCaching(connectionString, instance);
        services.AddSingleton<IdentityMetrics>();
        new IdentityLimitsValkeyModule().AddServices(services);
        return services.BuildServiceProvider();
    }

    private static string NewInstance() => $"test-{Guid.NewGuid():N}";

    [Test]
    public async Task Five_guesses_are_allowed_and_the_sixth_is_refused_with_the_time_left_in_the_window()
    {
        // Arrange
        await using var provider = Build(server.ConnectionString, NewInstance());
        var limiter = provider.GetRequiredService<IAttemptLimiter>();
        var code = Guid.NewGuid().ToString("N");

        // Act
        var first = new List<AttemptVerdict>();
        for (var i = 0; i < 5; i++)
        {
            first.Add(await limiter.HitAsync(CodeLimits.Guess, code, default));
        }

        var sixth = await limiter.HitAsync(CodeLimits.Guess, code, default);
        var otherCode = await limiter.HitAsync(CodeLimits.Guess, Guid.NewGuid().ToString("N"), default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.All(v => v.Outcome == AttemptOutcome.Allowed)).IsTrue();
            await Assert.That(sixth.Outcome).IsEqualTo(AttemptOutcome.Refused);
            await Assert.That(sixth.RetryAfter).IsGreaterThan(TimeSpan.Zero).And.IsLessThanOrEqualTo(TimeSpan.FromMinutes(15));
            await Assert.That(otherCode.Outcome).IsEqualTo(AttemptOutcome.Allowed);
        }
    }

    [Test]
    public async Task Parallel_guesses_never_exceed_the_limit_between_them()
    {
        // Arrange
        await using var provider = Build(server.ConnectionString, NewInstance());
        var limiter = provider.GetRequiredService<IAttemptLimiter>();
        var code = Guid.NewGuid().ToString("N");

        // Act
        var verdicts = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => limiter.HitAsync(CodeLimits.Guess, code, default)));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(verdicts.Count(v => v.Outcome == AttemptOutcome.Allowed)).IsEqualTo(5);
            await Assert.That(verdicts.Count(v => v.Outcome == AttemptOutcome.Refused)).IsEqualTo(20);
        }
    }

    [Test]
    public async Task The_count_lives_in_Valkey_under_the_module_flow_and_subject_and_every_replica_sees_it()
    {
        // Arrange
        var instance = NewInstance();
        await using var one = Build(server.ConnectionString, instance);
        await using var two = Build(server.ConnectionString, instance);
        var subject = Guid.NewGuid().ToString("N");

        // Act
        await one.GetRequiredService<IAttemptLimiter>().HitAsync(CodeLimits.Guess, subject, default);
        await one.GetRequiredService<IAttemptLimiter>().HitAsync(CodeLimits.Guess, subject, default);
        await two.GetRequiredService<IAttemptLimiter>().HitAsync(CodeLimits.Guess, subject, default);

        // Assert
        var key = $"{instance}:rate-limit:identity:code.guess:{subject}";
        var ttl = await server.Inspector.KeyTimeToLiveAsync(key);
        using (Assert.Multiple())
        {
            await Assert.That((long)await server.Inspector.StringGetAsync(key)).IsEqualTo(3);
            await Assert.That(ttl!.Value).IsGreaterThan(TimeSpan.FromMinutes(14)).And.IsLessThanOrEqualTo(TimeSpan.FromMinutes(15));
        }
    }

    [Test]
    public async Task A_setting_tightens_a_limit_and_a_setting_that_would_loosen_it_is_ignored()
    {
        // Arrange
        var settings = new Dictionary<string, string?>
        {
            ["Identity:Limits:code.guess:Limit"] = "2",
            ["Identity:Limits:code.send:Limit"] = "50",
            ["Identity:Limits:code.send:Window"] = "00:01:00",
        };
        await using var provider = Build(server.ConnectionString, NewInstance(), settings);
        var limiter = provider.GetRequiredService<IAttemptLimiter>();
        var code = Guid.NewGuid().ToString("N");
        var send = Guid.NewGuid().ToString("N");

        // Act
        var guesses = new List<AttemptOutcome>();
        for (var i = 0; i < 3; i++)
        {
            guesses.Add((await limiter.HitAsync(CodeLimits.Guess, code, default)).Outcome);
        }

        var sends = new List<AttemptVerdict>();
        for (var i = 0; i < 6; i++)
        {
            sends.Add(await limiter.HitAsync(CodeLimits.Send, send, default));
        }

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(guesses).IsEquivalentTo([AttemptOutcome.Allowed, AttemptOutcome.Allowed, AttemptOutcome.Refused]);
            await Assert.That(sends.Count(v => v.Outcome == AttemptOutcome.Allowed)).IsEqualTo(5);
            await Assert.That(sends[5].Outcome).IsEqualTo(AttemptOutcome.Refused);
            await Assert.That(sends[5].RetryAfter).IsGreaterThan(TimeSpan.FromMinutes(5));
        }
    }

    [Test]
    public async Task A_flow_that_fails_closed_refuses_while_Valkey_is_down_and_counts_it()
    {
        // Arrange
        await using var provider = Build(ValkeyServer.Unreachable, NewInstance());
        using var metrics = new MetricCapture(provider.GetRequiredService<IMeterFactory>());
        var limiter = provider.GetRequiredService<IAttemptLimiter>();

        // Act
        var first = await limiter.HitAsync(CodeLimits.Guess, "any", default);
        var second = await limiter.HitAsync(CodeLimits.Guess, "any", default);

        // Assert
        var counted = metrics.Of(IdentityMetrics.LimiterUnavailableMetric);
        using (Assert.Multiple())
        {
            await Assert.That(first.Outcome).IsEqualTo(AttemptOutcome.Unavailable);
            await Assert.That(second.Outcome).IsEqualTo(AttemptOutcome.Unavailable);
            await Assert.That(counted.Count).IsEqualTo(2);
            await Assert.That(counted[0].Tags[IdentityMetrics.FlowTag]).IsEqualTo("code.guess");
        }
    }

    [Test]
    public async Task A_flow_that_fails_open_lets_the_call_through_while_Valkey_is_down_and_leaves_it_alone_for_a_while()
    {
        // Arrange
        await using var provider = Build(ValkeyServer.Unreachable, NewInstance());
        using var metrics = new MetricCapture(provider.GetRequiredService<IMeterFactory>());
        var limiter = provider.GetRequiredService<IAttemptLimiter>();

        // Act
        var first = await limiter.HitAsync(CodeLimits.Send, "any", default);
        var second = await limiter.HitAsync(CodeLimits.Send, "any", default);
        var guess = await limiter.HitAsync(CodeLimits.Guess, "any", default);

        // Assert
        var counted = metrics.Of(IdentityMetrics.LimiterUnavailableMetric);
        using (Assert.Multiple())
        {
            await Assert.That(first.Outcome).IsEqualTo(AttemptOutcome.Allowed);
            await Assert.That(second.Outcome).IsEqualTo(AttemptOutcome.Allowed);
            await Assert.That(guess.Outcome).IsEqualTo(AttemptOutcome.Unavailable);
            await Assert.That(counted.Select(m => (string)m.Tags[IdentityMetrics.FlowTag]!)).IsEquivalentTo(["code.send", "code.guess"]);
        }
    }

    [Test]
    public async Task A_cancelled_caller_is_not_mistaken_for_an_outage()
    {
        // Arrange
        await using var provider = Build(server.ConnectionString, NewInstance());
        var limiter = provider.GetRequiredService<IAttemptLimiter>();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act
        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(async () => await limiter.HitAsync(Tiny, "any", cancelled.Token));

        // Assert
        await Assert.That(thrown).IsNotNull();
    }
}
