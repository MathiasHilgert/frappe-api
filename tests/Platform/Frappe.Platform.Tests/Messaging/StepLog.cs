using System.Collections.Concurrent;

namespace Frappe.Platform.Tests.Messaging;

/// <summary>Which steps ran, in order, for one test.</summary>
public sealed class StepLog
{
    private readonly ConcurrentQueue<string> _steps = new();

    public IReadOnlyList<string> Steps => [.. _steps];

    public void Add(string step) => _steps.Enqueue(step);
}
