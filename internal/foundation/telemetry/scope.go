package telemetry

import (
	"context"
	"fmt"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/metric"
	"go.opentelemetry.io/otel/trace"
)

// instrumentationScopePrefix identifies this application's instrumentation
// scopes to the OpenTelemetry SDK, separate from the metric and span name
// prefixes applied to individual instrument and span names.
const instrumentationScopePrefix = "github.com/MathiasHilgert/frappe-api/"

// metricNamePrefix is prepended to every metric name created through a
// Scope.
const metricNamePrefix = "frappe."

// countUnit is the default unit applied to counters and up-down counters.
const countUnit = "{count}"

// secondsUnit is the unit applied to Duration histograms.
const secondsUnit = "s"

// ModuleScope is a module's namespaced handle onto a meter and a tracer. It
// prefixes every metric name with "frappe.<module>." and every span name
// with "<module>.", and it works before the telemetry SDK's Up has run
// because it delegates to the global OpenTelemetry providers.
type ModuleScope struct {
	meter  metric.Meter
	tracer trace.Tracer
	module string
}

// Scope creates a ModuleScope for the given module name, validating that
// module matches the same lowercase, dot-separated naming rule as
// instrument names.
func Scope(module string) *ModuleScope {
	if err := validateName(module); err != nil {
		panic(err)
	}

	return &ModuleScope{
		module: module,
		meter:  otel.Meter(instrumentationScopePrefix + module),
		tracer: otel.Tracer(instrumentationScopePrefix + module),
	}
}

// metricName builds the fully-qualified, prefixed metric name for the
// given instrument name within this scope.
func (scope *ModuleScope) metricName(name string) string {
	return metricNamePrefix + scope.module + "." + name
}

// register validates name, builds its fully-qualified metric name,
// registers it in the process-wide duplicate registry, and returns the
// fully-qualified name.
func (scope *ModuleScope) register(name string) string {
	if err := validateName(name); err != nil {
		panic(err)
	}

	fullName := scope.metricName(name)
	registerInstrumentName(fullName)
	return fullName
}

// Counter creates a monotonic int64 counter with the default "{count}"
// unit.
func (scope *ModuleScope) Counter(name, description string) Counter {
	fullName := scope.register(name)

	instrument, err := scope.meter.Int64Counter(fullName, metric.WithDescription(description), metric.WithUnit(countUnit))
	if err != nil {
		panic(fmt.Errorf("telemetry: create counter %q: %w", fullName, err))
	}

	return Counter{instrument}
}

// UpDownCounter creates a non-monotonic int64 counter with the default
// "{count}" unit.
func (scope *ModuleScope) UpDownCounter(name, description string) UpDownCounter {
	fullName := scope.register(name)

	instrument, err := scope.meter.Int64UpDownCounter(fullName, metric.WithDescription(description), metric.WithUnit(countUnit))
	if err != nil {
		panic(fmt.Errorf("telemetry: create up-down counter %q: %w", fullName, err))
	}

	return UpDownCounter{instrument}
}

// Histogram creates a float64 histogram with the given unit.
func (scope *ModuleScope) Histogram(name, description, unit string) Histogram {
	fullName := scope.register(name)

	instrument, err := scope.meter.Float64Histogram(fullName, metric.WithDescription(description), metric.WithUnit(unit))
	if err != nil {
		panic(fmt.Errorf("telemetry: create histogram %q: %w", fullName, err))
	}

	return Histogram{instrument}
}

// Gauge creates a synchronous float64 gauge instrument.
func (scope *ModuleScope) Gauge(name, description string) Gauge {
	fullName := scope.register(name)

	instrument, err := scope.meter.Float64Gauge(fullName, metric.WithDescription(description))
	if err != nil {
		panic(fmt.Errorf("telemetry: create gauge %q: %w", fullName, err))
	}

	return Gauge{instrument}
}

// Duration creates a float64 histogram measured in seconds, exposing the
// Since convenience method in addition to the native Record method.
func (scope *ModuleScope) Duration(name, description string) Duration {
	fullName := scope.register(name)

	instrument, err := scope.meter.Float64Histogram(fullName, metric.WithDescription(description), metric.WithUnit(secondsUnit))
	if err != nil {
		panic(fmt.Errorf("telemetry: create duration histogram %q: %w", fullName, err))
	}

	return Duration{instrument}
}

// Start starts a span named "<module>.<name>" and returns the derived
// context together with the span.
func (scope *ModuleScope) Start(ctx context.Context, name string) (context.Context, trace.Span) {
	return scope.tracer.Start(ctx, scope.module+"."+name)
}
