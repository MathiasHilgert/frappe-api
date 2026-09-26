package query

import (
	"context"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/metric"
)

// searchMetrics are the geo business metrics; the use case RED metrics
// come from usecase.NewObserved.
//
//	Metric                     Kind       Attributes
//	frappe.geo.searches        counter    scope (places|countries|subdivisions|cities), outcome (matched|empty)
//	frappe.geo.search.results  histogram  scope; results on one search page
//
// Attributes are bounded: the query text is never an attribute.
type searchMetrics struct {
	searches metric.Int64Counter
	results  metric.Float64Histogram
}

// metrics are created once: an instrument name may only be registered
// once per meter.
var metrics = newSearchMetrics()

func newSearchMetrics() searchMetrics {
	meter := otel.Meter("github.com/MathiasHilgert/frappe-api/internal/modules/geo")
	searches, _ := meter.Int64Counter("frappe.geo.searches",
		metric.WithDescription("Place searches, by scope and outcome"), metric.WithUnit("{search}"))
	results, _ := meter.Float64Histogram("frappe.geo.search.results",
		metric.WithDescription("Results returned by one place search page"), metric.WithUnit("{result}"))
	return searchMetrics{searches: searches, results: results}
}

// record counts one search in scope that returned results places.
func (metrics searchMetrics) record(ctx context.Context, scope string, results int) {
	outcome := "matched"
	if results == 0 {
		outcome = "empty"
	}
	scopeAttribute := attribute.String("scope", scope)
	metrics.searches.Add(ctx, 1, metric.WithAttributes(scopeAttribute, attribute.String("outcome", outcome)))
	metrics.results.Record(ctx, float64(results), metric.WithAttributes(scopeAttribute))
}
