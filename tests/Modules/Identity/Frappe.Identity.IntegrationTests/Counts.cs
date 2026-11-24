using System.Diagnostics.Metrics;

namespace Frappe.Identity.IntegrationTests;

/// <summary>Counts what the module's counters record while the test runs, by their outcome or audience tag.</summary>
internal sealed class Counts : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<(string Name, string Tag, long Value)> _measurements = [];

    public Counts()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Frappe.Identity")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            lock (_measurements)
            {
                _measurements.Add((instrument.Name, tags.ToArray().Select(t => t.Value?.ToString()).FirstOrDefault() ?? string.Empty, value));
            }
        });
        _listener.Start();
    }

    public long Of(string name, string tag)
    {
        lock (_measurements)
        {
            return _measurements.Where(m => m.Name == name && m.Tag == tag).Sum(m => m.Value);
        }
    }

    public void Dispose() => _listener.Dispose();
}
