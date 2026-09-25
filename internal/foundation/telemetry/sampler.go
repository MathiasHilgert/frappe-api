package telemetry

import (
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
)

// tracesSamplerVariable is the standard OpenTelemetry environment variable
// the SDK reads (together with OTEL_TRACES_SAMPLER_ARG) whenever no
// sdktrace.WithSampler option is passed to sdktrace.NewTracerProvider.
const tracesSamplerVariable = "OTEL_TRACES_SAMPLER"

// productionTraceSampleRatio is the fraction of root traces kept in
// production. It is also used for any unrecognized environment, erring on
// the side of lower export cost.
const productionTraceSampleRatio = 0.1

// defaultTraceSampleRatio returns the head sampling ratio for root spans in
// the given deployment environment: every trace in development and
// staging, one in ten in production. The result is always within [0, 1].
func defaultTraceSampleRatio(deploymentEnvironment string) float64 {
	switch deploymentEnvironment {
	case "development", "staging":
		return 1.0
	default:
		return productionTraceSampleRatio
	}
}

// defaultSampler is parent-based so a service honors the sampling decision
// propagated by its caller, keeping distributed traces complete; only root
// spans are decided by the trace ID ratio.
func defaultSampler(deploymentEnvironment string) sdktrace.Sampler {
	return sdktrace.ParentBased(sdktrace.TraceIDRatioBased(defaultTraceSampleRatio(deploymentEnvironment)))
}

// samplerOptions returns the tracer provider options that select the
// sampler. When OTEL_TRACES_SAMPLER is set, no option is returned so the
// SDK applies the standard variables itself; otherwise the
// environment-derived default sampler is installed.
func samplerOptions(deploymentEnvironment string, lookup func(string) (string, bool)) []sdktrace.TracerProviderOption {
	if _, isSet := lookup(tracesSamplerVariable); isSet {
		return nil
	}
	return []sdktrace.TracerProviderOption{sdktrace.WithSampler(defaultSampler(deploymentEnvironment))}
}
