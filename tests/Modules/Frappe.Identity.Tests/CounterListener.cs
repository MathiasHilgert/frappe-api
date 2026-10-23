using System.Diagnostics.Metrics;
using Frappe.Identity.Infrastructure.Security;

namespace Frappe.Identity.Tests;

/// <summary>Sums one identity counter while alive, so a test can assert that a degraded path was counted.</summary>
internal sealed class CounterListener : IDisposable
{
    private readonly MeterListener _listener = new();
    private long _total;

    public CounterListener(string instrument)
    {
        _listener.InstrumentPublished = (published, listener) =>
        {
            if (published.Meter.Name == IdentityMetrics.MeterName && published.Name == instrument)
                listener.EnableMeasurementEvents(published);
        };
        _listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref _total, value));
        _listener.Start();
    }

    public long Total => Interlocked.Read(ref _total);

    public void Dispose() => _listener.Dispose();
}
