using Frappe.Testing;

namespace Frappe.Architecture.Tests;

/// <summary>
/// The collector decides which traces to keep (OBS-13): every span arrives from the processes, and its <c>tail_sampling</c>
/// processor keeps all error traces and all slow ones plus a share of the rest. Checked without a collector, by the keys of
/// its configuration.
/// </summary>
public sealed class CollectorSamplingTests
{
    private const string Collector = "deploy/observability/otel-collector.yaml";

    private static string Config() => File.ReadAllText(Path.Combine(RepositoryRoot.FullPath, Collector));

    [Test]
    public async Task The_trace_pipeline_samples_after_it_has_seen_the_whole_trace()
    {
        // Act
        var traces = Config().Split('\n').Single(
            line => line.TrimStart().StartsWith("traces:", StringComparison.Ordinal) && line.Contains("receivers", StringComparison.Ordinal));

        // Assert
        await Assert.That(traces).Contains("processors: [memory_limiter, tail_sampling, batch]");
    }

    [Test]
    public async Task Every_error_trace_and_every_slow_trace_is_kept_whatever_the_share_of_the_rest()
    {
        // Act
        var config = Config();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(config).Contains("type: status_code");
            await Assert.That(config).Contains("status_codes: [ERROR]");
            await Assert.That(config).Contains("type: latency");
            await Assert.That(config).Contains("threshold_ms: 1000");
            await Assert.That(config).Contains("type: probabilistic");
        }
    }

    [Test]
    public async Task The_share_of_the_other_traces_changes_in_the_collector_not_in_a_process()
    {
        // Act
        var config = Config();

        // Assert
        await Assert.That(config).Contains("sampling_percentage: ${env:OTEL_TAIL_SAMPLING_PERCENTAGE:-10}");
    }
}
