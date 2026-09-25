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
package telemetry
