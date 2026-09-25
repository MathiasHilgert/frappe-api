package telemetry

import (
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
)

// setStartInstrumentationForTest substitutes startInstrumentation for the
// duration of a test, returning a function that restores the original
// implementation. It exists so tests can force Up's post-providers step to
// fail and prove Up's cleanup behavior, without any real instrumentation
// package offering a way to fail on demand.
func setStartInstrumentationForTest(fn func(*sdkmetric.MeterProvider) error) (restore func()) {
	previous := startInstrumentation
	startInstrumentation = fn
	return func() { startInstrumentation = previous }
}
