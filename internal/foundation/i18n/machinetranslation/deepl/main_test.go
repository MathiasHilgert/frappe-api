package deepl_test

import (
	"context"
	"os"
	"testing"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	"go.opentelemetry.io/otel/sdk/metric/metricdata"
)

// metricReader observes the instruments the Client creates through the
// global OpenTelemetry API.
var metricReader = sdkmetric.NewManualReader()

func TestMain(m *testing.M) {
	otel.SetMeterProvider(sdkmetric.NewMeterProvider(sdkmetric.WithReader(metricReader)))
	os.Exit(m.Run())
}

// counterValue sums the points of counter whose attributes include every
// key and value of want.
func counterValue(t *testing.T, counter string, want map[string]string) int64 {
	t.Helper()
	var data metricdata.ResourceMetrics
	if err := metricReader.Collect(context.Background(), &data); err != nil {
		t.Fatal(err)
	}
	var total int64
	for _, scope := range data.ScopeMetrics {
		for _, found := range scope.Metrics {
			if found.Name != counter {
				continue
			}
			for _, point := range found.Data.(metricdata.Sum[int64]).DataPoints {
				matches := true
				for key, value := range want {
					got, _ := point.Attributes.Value(attribute.Key(key))
					matches = matches && got.AsString() == value
				}
				if matches {
					total += point.Value
				}
			}
		}
	}
	return total
}
