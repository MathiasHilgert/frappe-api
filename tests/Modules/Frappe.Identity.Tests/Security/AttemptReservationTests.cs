using Frappe.Identity.Application.Security;
using Shouldly;

namespace Frappe.Identity.Tests.Security;

/// <summary>Reserve-first gating over several limits: all are checked, all are released when one refuses.</summary>
public sealed class AttemptReservationTests
{
    private static readonly AttemptPolicy Short = new("short", 1, TimeSpan.FromMinutes(1));
    private static readonly AttemptPolicy Long = new("long", 1, TimeSpan.FromHours(1));
    private static readonly AttemptPolicy Open = new("open", 1, TimeSpan.FromMinutes(5));

    [Fact]
    public async Task SeveralRefusals_AnswerTheLongestRetryAfter()
    {
        var limiter = new ScriptedLimiter { [Short] = TimeSpan.FromSeconds(30), [Long] = TimeSpan.FromMinutes(40) };

        var reservation = await AttemptReservation.TakeAsync(limiter, [(Short, "a"), (Long, "b"), (Open, "c")], TestContext.Current.CancellationToken);

        reservation.IsAllowed.ShouldBeFalse();
        reservation.Decision.RetryAfter.ShouldBe(TimeSpan.FromMinutes(40));
        limiter.Reserved.ShouldBe(["short:a", "long:b", "open:c"]);
    }

    [Fact]
    public async Task ARefusal_ReleasesEverySlotThatWasTaken()
    {
        var limiter = new ScriptedLimiter { [Long] = TimeSpan.FromMinutes(1) };

        await AttemptReservation.TakeAsync(limiter, [(Short, "a"), (Long, "b"), (Open, "c")], TestContext.Current.CancellationToken);

        limiter.Released.ShouldBe(["short:a", "open:c"]);
    }

    [Fact]
    public async Task Compensations_IgnoreTheRequestsCancellation_SoNoSlotLeaks()
    {
        var limiter = new ScriptedLimiter { [Long] = TimeSpan.FromMinutes(1) };
        using var cancelled = new CancellationTokenSource();
        limiter.OnReserve = () => cancelled.Cancel();

        await AttemptReservation.TakeAsync(limiter, [(Short, "a"), (Long, "b")], cancelled.Token);

        limiter.Released.ShouldBe(["short:a"]);
        limiter.ReleaseTokens.ShouldAllBe(t => !t.CanBeCanceled);
    }

    [Fact]
    public async Task ReleaseAndSucceeded_IgnoreCancellation()
    {
        var limiter = new ScriptedLimiter();
        var reservation = await AttemptReservation.TakeAsync(limiter, [(Short, "a"), (Long, "b")], TestContext.Current.CancellationToken);

        await reservation.SucceededAsync(Short);
        await reservation.ReleaseAsync();

        limiter.Resets.ShouldBe(["short:a"]);
        limiter.Released.ShouldBe(["long:b", "short:a", "long:b"]);
        limiter.ReleaseTokens.ShouldAllBe(t => !t.CanBeCanceled);
    }

    private sealed class ScriptedLimiter : Dictionary<AttemptPolicy, TimeSpan>, IAttemptLimiter
    {
        public List<string> Reserved { get; } = [];
        public List<string> Released { get; } = [];
        public List<string> Resets { get; } = [];
        public List<CancellationToken> ReleaseTokens { get; } = [];
        public Action? OnReserve { get; set; }

        public Task<AttemptDecision> ReserveAsync(AttemptPolicy policy, string key, CancellationToken cancellationToken)
        {
            OnReserve?.Invoke();
            Reserved.Add($"{policy.Name}:{key}");
            return Task.FromResult(TryGetValue(policy, out var wait)
                ? new AttemptDecision(false, 0, wait)
                : AttemptDecision.Allow(0));
        }

        public Task ReleaseAsync(AttemptPolicy policy, string key, CancellationToken cancellationToken)
        {
            Released.Add($"{policy.Name}:{key}");
            ReleaseTokens.Add(cancellationToken);
            return Task.CompletedTask;
        }

        public Task ResetAsync(AttemptPolicy policy, string key, CancellationToken cancellationToken)
        {
            Resets.Add($"{policy.Name}:{key}");
            ReleaseTokens.Add(cancellationToken);
            return Task.CompletedTask;
        }

        public Task<AttemptDecision> RecordAsync(AttemptPolicy policy, string key, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
