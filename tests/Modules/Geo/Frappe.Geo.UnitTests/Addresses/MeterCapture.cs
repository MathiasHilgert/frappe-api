using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Frappe.Geo.UnitTests.Addresses;

/// <summary>Every measurement of one meter instance, with its tags.</summary>
internal sealed class MeterCapture : IDisposable
{
    private readonly MeterListener _listener;

    public MeterCapture(Meter meter)
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, meter))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument, value, tags));
        _listener.Start();
    }

    public ConcurrentQueue<(string Name, double Value, Dictionary<string, object?> Tags)> Measurements { get; } = new();

    public double Sum(string name, params (string Key, string Value)[] tags) =>
        Measurements.Where(m => m.Name == name && tags.All(t => Equals(m.Tags.GetValueOrDefault(t.Key), t.Value))).Sum(m => m.Value);

    public void Dispose() => _listener.Dispose();

    private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        Measurements.Enqueue((instrument.Name, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value)));
}
