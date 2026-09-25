// Package telemetry is the foundation layer for observability: tracing,
// metrics and logging. It wires the OpenTelemetry SDK into the
// application lifecycle as an application.Dependency, and exposes a thin
// set of DX helpers on top of the native OpenTelemetry API. The OpenTelemetry
// API stays the abstraction; this package only removes boilerplate around
// instrument creation and naming.
//
// # Module convention
//
// Each business module that wants telemetry declares two subpackages:
//
//	internal/modules/<module>/metrics/metrics.go:
//
//		package metrics
//
//		import "github.com/MathiasHilgert/frappe-api/internal/foundation/telemetry"
//
//		var scope = telemetry.Scope("orders")
//
//		var OrdersCreated = scope.Counter("created", "Orders created")
//
//	internal/modules/<module>/tracing/tracing.go:
//
//		package tracing
//
//		import (
//			"context"
//
//			"go.opentelemetry.io/otel/trace"
//
//			"github.com/MathiasHilgert/frappe-api/internal/foundation/telemetry"
//		)
//
//		var scope = telemetry.Scope("orders")
//
//		func Start(ctx context.Context, name string) (context.Context, trace.Span) {
//			return scope.Start(ctx, name)
//		}
//
// Application code then calls the module's own thin wrappers:
//
//	metrics.OrdersCreated.Add(ctx, 1)
//	ctx, span := tracing.Start(ctx, "create")
//	defer span.End()
//
// Instruments are safe to create at package-initialization time, before
// the telemetry Dependency's Up has run: they delegate to the global
// OpenTelemetry providers, which are no-op until Up installs the real
// SDK-backed providers.
//
// # Trace sampling
//
// Sampling here is head sampling: the keep-or-drop decision is made when a
// trace starts. The default sampler is
// sdktrace.ParentBased(sdktrace.TraceIDRatioBased(ratio)): a span with a
// remote or local parent follows its parent's decision, so a distributed
// trace is kept or dropped consistently across services, and only root
// spans are decided by the trace ID ratio. The ratio derives from
// APPLICATION_ENVIRONMENT: 1.0 in development and staging, 0.1 in
// production (and in any unrecognized environment).
//
// The standard OpenTelemetry variables override this default. When
// OTEL_TRACES_SAMPLER is set, no sampler option is passed and the SDK
// reads OTEL_TRACES_SAMPLER and OTEL_TRACES_SAMPLER_ARG itself, for
// example:
//
//	OTEL_TRACES_SAMPLER=parentbased_traceidratio
//	OTEL_TRACES_SAMPLER_ARG=0.25
//
// An invalid value makes the SDK log an error and fall back to its own
// default (parentbased_always_on), not to the environment-derived ratio.
//
// Exemplars keep working: the metric SDK's default trace-based exemplar
// filter only attaches measurements recorded inside sampled spans, so a
// lower ratio means proportionally fewer exemplars linking metrics to
// traces.
//
// Tail sampling (always keeping error or slow traces while dropping the
// rest) cannot be decided in-process and belongs in an OpenTelemetry
// Collector or Grafana Alloy pipeline in front of the backend; that is a
// possible future addition, not something this package does.
package telemetry
