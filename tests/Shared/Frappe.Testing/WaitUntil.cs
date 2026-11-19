namespace Frappe.Testing;

/// <summary>
/// The only place a test waits for an eventual effect (TEST-13): it polls a condition until it holds or a timeout passes,
/// and then fails with a message saying what never happened. Tests never call <c>Task.Delay</c> or <c>Thread.Sleep</c>.
/// </summary>
public static class WaitUntil
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>Waits until the asynchronous <paramref name="condition"/> is true (a query, a key lookup); throws <see cref="TimeoutException"/> with <paramref name="failure"/> after <paramref name="timeout"/> (30 s by default).</summary>
    public static Task TrueAsync(Func<Task<bool>> condition, string failure, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failure);
        return TrueAsync(condition, () => Task.FromResult(failure), timeout);
    }

    /// <summary>Waits until the asynchronous <paramref name="condition"/> is true; the message of the <see cref="TimeoutException"/> is read from <paramref name="failure"/> only when the wait expires (it may gather logs).</summary>
    public static async Task TrueAsync(Func<Task<bool>> condition, Func<Task<string>> failure, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(failure);
        using var expired = new CancellationTokenSource(timeout ?? DefaultTimeout);
        while (!await condition())
        {
            try
            {
                await Task.Delay(PollInterval, expired.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(await failure());
            }
        }
    }

    /// <summary>Waits until <paramref name="condition"/> is true; throws <see cref="TimeoutException"/> with <paramref name="failure"/> after <paramref name="timeout"/> (30 s by default).</summary>
    public static async Task TrueAsync(Func<bool> condition, string failure, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentException.ThrowIfNullOrWhiteSpace(failure);
        using var expired = new CancellationTokenSource(timeout ?? DefaultTimeout);
        while (!condition())
        {
            try
            {
                await Task.Delay(PollInterval, expired.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(failure);
            }
        }
    }
}
