package httpserver_test

import (
	"context"
	"errors"
	"fmt"
	"net"
	"net/http"
	"net/http/httptest"
	"sync"
	"testing"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	"go.opentelemetry.io/otel/sdk/metric/metricdata"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/httpserver"
)

// The OpenTelemetry global meter binds httpserver's package-level
// instruments to the first MeterProvider installed for the lifetime of
// the process, so every rate limit metric test shares one provider. Its
// reader uses delta temporality: each collect returns only what was
// recorded since the previous one, so tests stay independent as long as
// they drain the reader first (they never run in parallel).
var (
	sharedMetricReader     *sdkmetric.ManualReader
	sharedMetricReaderOnce sync.Once
)

func rateLimitMetricReader(t *testing.T) *sdkmetric.ManualReader {
	t.Helper()
	sharedMetricReaderOnce.Do(func() {
		sharedMetricReader = sdkmetric.NewManualReader(sdkmetric.WithTemporalitySelector(
			func(sdkmetric.InstrumentKind) metricdata.Temporality { return metricdata.DeltaTemporality },
		))
		otel.SetMeterProvider(sdkmetric.NewMeterProvider(sdkmetric.WithReader(sharedMetricReader)))
	})
	collectMetrics(t, sharedMetricReader)
	return sharedMetricReader
}

func collectMetrics(t *testing.T, reader *sdkmetric.ManualReader) map[string]metricdata.Aggregation {
	t.Helper()
	var data metricdata.ResourceMetrics
	if err := reader.Collect(context.Background(), &data); err != nil {
		t.Fatalf("collect: %v", err)
	}
	metrics := map[string]metricdata.Aggregation{}
	for _, scopeMetrics := range data.ScopeMetrics {
		for _, metricValue := range scopeMetrics.Metrics {
			metrics[metricValue.Name] = metricValue.Data
		}
	}
	return metrics
}

// attributeMap flattens a data point attribute set for comparison.
func attributeMap(set attribute.Set) map[string]string {
	values := map[string]string{}
	for _, keyValue := range set.ToSlice() {
		values[string(keyValue.Key)] = keyValue.Value.AsString()
	}
	return values
}

func assertAttributes(t *testing.T, name string, got attribute.Set, want map[string]string) {
	t.Helper()
	values := attributeMap(got)
	if fmt.Sprint(values) != fmt.Sprint(want) {
		t.Fatalf("%s attributes = %v, want %v", name, values, want)
	}
}

func singleCounterPoint(t *testing.T, metrics map[string]metricdata.Aggregation, name string) metricdata.DataPoint[int64] {
	t.Helper()
	sum, ok := metrics[name].(metricdata.Sum[int64])
	if !ok || len(sum.DataPoints) != 1 {
		t.Fatalf("%s = %#v, want exactly one int64 sum data point", name, metrics[name])
	}
	return sum.DataPoints[0]
}

func singleHistogramPoint(t *testing.T, metrics map[string]metricdata.Aggregation, name string) metricdata.HistogramDataPoint[float64] {
	t.Helper()
	histogram, ok := metrics[name].(metricdata.Histogram[float64])
	if !ok || len(histogram.DataPoints) != 1 {
		t.Fatalf("%s = %#v, want exactly one float64 histogram data point", name, metrics[name])
	}
	return histogram.DataPoints[0]
}

func TestRateLimitMetricsCarryOutcomeAndRouteTemplate(t *testing.T) {
	cases := []struct {
		name     string
		outcome  string
		decision httpserver.RateLimitDecision
	}{
		{name: "allowed", outcome: "allowed", decision: httpserver.RateLimitDecision{Allowed: true, Limit: 10, Remaining: 9}},
		{name: "limited", outcome: "limited", decision: httpserver.RateLimitDecision{Allowed: false, Limit: 10}},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			reader := rateLimitMetricReader(t)
			limiter := &fakeLimiter{decision: testCase.decision}
			server := rateLimitedServer(t, nil, httpserver.RateLimitSettings{Limiter: limiter})

			serve(server, httptest.NewRequest(http.MethodGet, "/v1/ping", nil))

			metrics := collectMetrics(t, reader)
			want := map[string]string{"outcome": testCase.outcome, "http.route": "/v1/ping"}
			decision := singleCounterPoint(t, metrics, "frappe.rate_limit.decisions")
			assertAttributes(t, "frappe.rate_limit.decisions", decision.Attributes, want)
			if decision.Value != 1 {
				t.Fatalf("decisions value = %d, want 1", decision.Value)
			}
			duration := singleHistogramPoint(t, metrics, "frappe.rate_limit.duration")
			assertAttributes(t, "frappe.rate_limit.duration", duration.Attributes, want)
			if duration.Count != 1 {
				t.Fatalf("duration count = %d, want 1", duration.Count)
			}
		})
	}
}

func TestRateLimitMetricsUseTheUnmatchedRouteNeverTheRawPath(t *testing.T) {
	reader := rateLimitMetricReader(t)
	limiter := &fakeLimiter{decision: httpserver.RateLimitDecision{Allowed: true, Limit: 10, Remaining: 9}}
	server := rateLimitedServer(t, nil, httpserver.RateLimitSettings{Limiter: limiter})

	serve(server, httptest.NewRequest(http.MethodGet, "/v1/does-not-exist/12345", nil))

	decision := singleCounterPoint(t, collectMetrics(t, reader), "frappe.rate_limit.decisions")
	assertAttributes(t, "frappe.rate_limit.decisions", decision.Attributes,
		map[string]string{"outcome": "allowed", "http.route": "unmatched"})
}

// timeoutError is a net.Error reporting a timeout, as a dialer or socket
// deadline produces.
type timeoutError struct{}

func (timeoutError) Error() string   { return "i/o timeout" }
func (timeoutError) Timeout() bool   { return true }
func (timeoutError) Temporary() bool { return false }

var _ net.Error = timeoutError{}

func TestRateLimitErrorMetricsClassifyTheReason(t *testing.T) {
	cases := []struct {
		err    error
		name   string
		reason string
	}{
		{name: "deadline exceeded", err: fmt.Errorf("allow: %w", context.DeadlineExceeded), reason: "timeout"},
		{name: "network timeout", err: fmt.Errorf("allow: %w", timeoutError{}), reason: "timeout"},
		{name: "connection refused", err: errors.New("connection refused"), reason: "error"},
		{name: "canceled", err: context.Canceled, reason: "error"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			reader := rateLimitMetricReader(t)
			limiter := &fakeLimiter{err: testCase.err}
			server := rateLimitedServer(t, nil, httpserver.RateLimitSettings{Limiter: limiter})

			serve(server, httptest.NewRequest(http.MethodGet, "/v1/ping", nil))

			metrics := collectMetrics(t, reader)
			failure := singleCounterPoint(t, metrics, "frappe.rate_limit.errors")
			assertAttributes(t, "frappe.rate_limit.errors", failure.Attributes,
				map[string]string{"reason": testCase.reason, "http.route": "/v1/ping"})
			duration := singleHistogramPoint(t, metrics, "frappe.rate_limit.duration")
			assertAttributes(t, "frappe.rate_limit.duration", duration.Attributes,
				map[string]string{"outcome": "error", "http.route": "/v1/ping"})
			if _, found := metrics["frappe.rate_limit.decisions"]; found {
				t.Fatal("frappe.rate_limit.decisions recorded for a limiter error")
			}
		})
	}
}
