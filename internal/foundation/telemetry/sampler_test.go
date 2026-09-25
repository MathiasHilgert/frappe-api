package telemetry

import (
	"context"
	"testing"

	sdktrace "go.opentelemetry.io/otel/sdk/trace"
)

func TestDefaultTraceSampleRatioPerEnvironment(t *testing.T) {
	cases := map[string]float64{
		"development": 1.0,
		"staging":     1.0,
		"production":  0.1,
		"unknown":     0.1,
	}
	for environment, want := range cases {
		got := defaultTraceSampleRatio(environment)
		if got != want {
			t.Errorf("defaultTraceSampleRatio(%q) = %v, want %v", environment, got, want)
		}
		if got < 0 || got > 1 {
			t.Errorf("defaultTraceSampleRatio(%q) = %v, outside [0, 1]", environment, got)
		}
	}
}

func TestSamplerOptionsUseParentBasedRatioWhenSamplerVariableIsUnset(t *testing.T) {
	lookup := func(string) (string, bool) { return "", false }

	options := samplerOptions("production", lookup)
	if len(options) != 1 {
		t.Fatalf("len(samplerOptions) = %d, want 1", len(options))
	}

	want := sdktrace.ParentBased(sdktrace.TraceIDRatioBased(0.1)).Description()
	if got := defaultSampler("production").Description(); got != want {
		t.Fatalf("defaultSampler description = %q, want %q", got, want)
	}
}

func TestSamplerOptionsDeferToStandardEnvironmentVariableWhenSet(t *testing.T) {
	t.Setenv("OTEL_TRACES_SAMPLER", "always_off")

	options := samplerOptions("development", func(key string) (string, bool) {
		if key == "OTEL_TRACES_SAMPLER" {
			return "always_off", true
		}
		return "", false
	})
	if len(options) != 0 {
		t.Fatalf("len(samplerOptions) = %d, want 0 so the SDK reads OTEL_TRACES_SAMPLER", len(options))
	}

	provider := sdktrace.NewTracerProvider(options...)
	defer func() { _ = provider.Shutdown(context.Background()) }()

	_, span := provider.Tracer("sampler-test").Start(context.Background(), "span")
	defer span.End()
	if span.SpanContext().IsSampled() {
		t.Fatal("span is sampled, want OTEL_TRACES_SAMPLER=always_off to be honored")
	}
}

func TestDefaultSamplerSamplesEverythingInDevelopment(t *testing.T) {
	provider := sdktrace.NewTracerProvider(samplerOptions("development", func(string) (string, bool) { return "", false })...)
	defer func() { _ = provider.Shutdown(context.Background()) }()

	_, span := provider.Tracer("sampler-test").Start(context.Background(), "span")
	defer span.End()
	if !span.SpanContext().IsSampled() {
		t.Fatal("span is not sampled, want ratio 1.0 in development")
	}
}
