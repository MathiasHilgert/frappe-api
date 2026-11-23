using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Frappe.Persistence;

namespace Frappe.Platform.IntegrationTests.Persistence;

/// <summary>
/// The measurements of the persistence meter made through one <see cref="IMeterFactory"/>, so a test never sees what a test running
/// beside it measured.
/// </summary>
internal sealed class PersistenceMeterCapture : IDisposable
{
    private readonly MeterListener _listener = new();

    public PersistenceMeterCapture(IMeterFactory factory)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == PersistenceMetrics.MeterName && ReferenceEquals(instrument.Meter.Scope, factory))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.Start();
    }

    public ConcurrentBag<Measurement> Measurements { get; } = [];

    /// <summary>What was measured on the instrument <paramref name="name"/>.</summary>
    public Measurement[] Of(string name) => [.. Measurements.Where(measurement => measurement.Instrument == name)];

    public void Dispose() => _listener.Dispose();

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        Measurements.Add(new Measurement(instrument.Name, value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value)));

    /// <summary>One recorded measurement: the instrument, its value and tags.</summary>
    internal sealed record Measurement(string Instrument, double Value, Dictionary<string, object?> Tags);
}
