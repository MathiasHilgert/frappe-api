using System.Collections.Concurrent;
using Frappe.TestUtilities;

namespace Frappe.Platform.IntegrationTests.Messaging;

// How many times a fixture handler ran for one id; ids are unique per test, so tests never share a count.
internal static class Runs
{
    private static readonly ConcurrentDictionary<string, int> _runs = new(StringComparer.Ordinal);

    // Completed by the n-th run, for tests whose trigger is no message they send (a broker coming back).
    private static readonly ConcurrentDictionary<string, TaskCompletionSource> _waits = new(
        StringComparer.Ordinal
    );

    public static int Record(Guid id, string handler = "")
    {
        int runs = _runs.AddOrUpdate($"{handler}{id}", 1, static (_, count) => count + 1);
        Wait($"{handler}{id}#{runs}").TrySetResult();
        return runs;
    }

    public static Task WaitForAsync(Guid id, string handler = "", int runs = 1) =>
        Wait($"{handler}{id}#{runs}")
            .Task.WaitAsync(TrackedMessages.Timeout, TestContext.Current.CancellationToken);

    public static int Of(Guid id, string handler = "") =>
        _runs.TryGetValue($"{handler}{id}", out int runs) ? runs : 0;

    private static TaskCompletionSource Wait(string key) =>
        _waits.GetOrAdd(
            key,
            static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        );
}
