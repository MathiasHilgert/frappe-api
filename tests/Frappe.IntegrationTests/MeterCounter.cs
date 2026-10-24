using System.Diagnostics.Metrics;

namespace Frappe.IntegrationTests;

/// <summary>Sums one counter of one meter while alive. Hosts run in parallel, so assert a lower bound.</summary>
internal sealed class MeterCounter : IDisposable
{
    private readonly MeterListener _listener = new();
    private long _total;

    public MeterCounter(string meter, string instrument)
    {
        _listener.InstrumentPublished = (published, listener) =>
        {
            if (published.Meter.Name == meter && published.Name == instrument)
                listener.EnableMeasurementEvents(published);
        };
        _listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref _total, value));
        _listener.Start();
    }

    public long Total => Interlocked.Read(ref _total);

    public void Dispose() => _listener.Dispose();
}
