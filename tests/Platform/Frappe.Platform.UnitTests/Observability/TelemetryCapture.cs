using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Frappe.Observability;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>Captures spans, handler duration measurements and log entries emitted while a test runs.</summary>
internal sealed class TelemetryCapture : IDisposable
{
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener;

    public TelemetryCapture()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FrappeTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = Activities.Add,
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == FrappeTelemetry.Name && instrument.Name == FrappeTelemetry.HandlerDurationMetric)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _meterListener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
            Measurements.Add(new Measurement(value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        _meterListener.Start();
    }

    public ConcurrentBag<Activity> Activities { get; } = [];

    public ConcurrentBag<Measurement> Measurements { get; } = [];

    public ConcurrentBag<(LogLevel Level, string Message)> Logs { get; } = [];

    public Activity SingleSpan(string name) => Activities.Single(a => a.DisplayName == name);

    public Measurement SingleMeasurement(string useCase) =>
        Measurements.Single(m => (string?)m.Tags[FrappeTelemetry.Tags.UseCase] == useCase);

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
    }

    /// <summary>One recorded measurement: its value and tags.</summary>
    internal sealed record Measurement(double Value, Dictionary<string, object?> Tags);
}
