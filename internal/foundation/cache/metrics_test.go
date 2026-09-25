package cache_test

import (
	"context"
	"os"
	"testing"
	"time"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	"go.opentelemetry.io/otel/sdk/metric/metricdata"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/cache"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/cache/memory"
)

// metricReader observes the instruments cache creates through the global
// OpenTelemetry API; the provider is installed once for the package.
var metricReader = sdkmetric.NewManualReader()

func TestMain(m *testing.M) {
	otel.SetMeterProvider(sdkmetric.NewMeterProvider(sdkmetric.WithReader(metricReader)))
	os.Exit(m.Run())
}

// counterValue sums the data points of the named counter whose attributes
// include every wanted attribute.
func counterValue(t *testing.T, name string, wanted ...attribute.KeyValue) int64 {
	t.Helper()
	var collected metricdata.ResourceMetrics
	if err := metricReader.Collect(context.Background(), &collected); err != nil {
		t.Fatalf("collect metrics: %v", err)
	}
	var total int64
	for _, scope := range collected.ScopeMetrics {
		for _, instrument := range scope.Metrics {
			if instrument.Name != name {
				continue
			}
			sum, _ := instrument.Data.(metricdata.Sum[int64])
			for _, point := range sum.DataPoints {
				if hasAll(point.Attributes, wanted) {
					total += point.Value
				}
			}
		}
	}
	return total
}

func hasAll(set attribute.Set, wanted []attribute.KeyValue) bool {
	for _, want := range wanted {
		if value, found := set.Value(want.Key); !found || value != want.Value {
			return false
		}
	}
	return true
}

func TestRequestsAreCountedPerModuleAndOutcome(t *testing.T) {
	module := attribute.String("module", "metered")
	hits := counterValue(t, "frappe.cache.requests", module, attribute.String("outcome", "hit"))
	misses := counterValue(t, "frappe.cache.requests", module, attribute.String("outcome", "miss"))

	byID := cache.New(newBackend(memory.NewStore()), uniqueName("metered"), time.Minute,
		func(context.Context, int) (menu, error) { return menu{}, nil })
	for range 3 {
		if _, err := byID.Get(acme(), 1); err != nil {
			t.Fatalf("Get returned error: %v", err)
		}
	}

	if got := counterValue(t, "frappe.cache.requests", module, attribute.String("outcome", "miss")) - misses; got != 1 {
		t.Fatalf("misses = %d; want 1", got)
	}
	if got := counterValue(t, "frappe.cache.requests", module, attribute.String("outcome", "hit")) - hits; got != 2 {
		t.Fatalf("hits = %d; want 2", got)
	}
}

func TestFailuresAreCountedByOperationAndReason(t *testing.T) {
	missingTenant := []attribute.KeyValue{attribute.String("operation", "key"), attribute.String("reason", "missing_tenant")}
	storeGet := []attribute.KeyValue{attribute.String("operation", "get"), attribute.String("reason", "store_error")}
	before := counterValue(t, "frappe.cache.errors", missingTenant...)
	beforeGet := counterValue(t, "frappe.cache.errors", storeGet...)

	load := func(context.Context, int) (menu, error) { return menu{}, nil }
	if _, err := cache.New(newBackend(memory.NewStore()), uniqueName("metered"), time.Minute, load).Get(context.Background(), 1); err != nil {
		t.Fatalf("Get returned error: %v", err)
	}
	if _, err := cache.New(newBackend(failingStore{}), uniqueName("metered"), time.Minute, load).Get(acme(), 1); err != nil {
		t.Fatalf("Get returned error: %v", err)
	}

	if got := counterValue(t, "frappe.cache.errors", missingTenant...) - before; got != 1 {
		t.Fatalf("missing tenant errors = %d; want 1", got)
	}
	if got := counterValue(t, "frappe.cache.errors", storeGet...) - beforeGet; got != 1 {
		t.Fatalf("store get errors = %d; want 1", got)
	}
}
