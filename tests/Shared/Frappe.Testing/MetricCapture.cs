using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Frappe.Testing;

/// <summary>Every measurement of the instruments created through one <see cref="IMeterFactory"/>, so parallel tests never see each other's.</summary>
public sealed class MetricCapture : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<CapturedMeasurement> _measurements = new();

    public MetricCapture(IMeterFactory factory)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Scope == factory)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument, value, tags));
        _listener.Start();
    }

    /// <summary>The measurements of <paramref name="instrument"/> so far, in order.</summary>
    public IReadOnlyList<CapturedMeasurement> Of(string instrument) => [.. _measurements.Where(m => m.Instrument == instrument)];

    /// <summary>Collects the observable instruments (gauges) now, as an export would.</summary>
    public void Observe() => _listener.RecordObservableInstruments();

    public void Dispose() => _listener.Dispose();

    private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, tag) in tags)
        {
            copy[key] = tag;
        }

        _measurements.Enqueue(new CapturedMeasurement(instrument.Name, value, copy));
    }
}
