using System.Collections.Concurrent;

namespace Frappe.Platform.IntegrationTests.Messaging;

// How many times a fixture handler ran for one id; ids are unique per test, so tests never share a count.
internal static class Runs
{
    private static readonly ConcurrentDictionary<string, int> _runs = new(StringComparer.Ordinal);

    public static int Record(Guid id, string handler = "") =>
        _runs.AddOrUpdate($"{handler}{id}", 1, static (_, runs) => runs + 1);

    public static int Of(Guid id, string handler = "") =>
        _runs.TryGetValue($"{handler}{id}", out int runs) ? runs : 0;
}
