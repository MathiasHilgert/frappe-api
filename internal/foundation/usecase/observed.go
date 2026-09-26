package usecase

import (
	"context"
	"errors"
	"log/slog"
	"time"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/codes"
	"go.opentelemetry.io/otel/metric"
	"go.opentelemetry.io/otel/trace"
)

// Outcomes of one handler call, the "outcome" metric attribute.
const (
	outcomeSuccess  = "success"
	outcomeRejected = "rejected"
	outcomeError    = "error"
)

// The RED instruments shared by every observed handler, told apart by
// their bounded "handler" attribute:
//
//	frappe.usecase.calls     counter    handler, outcome (success|rejected|error)
//	frappe.usecase.duration  histogram  handler, outcome; seconds
//
// They use the global OpenTelemetry API directly, like the other general
// foundation packages (go-arch-lint keeps them from importing each other).
var (
	meter    = otel.Meter(instrumentationScope)
	calls, _ = meter.Int64Counter("frappe.usecase.calls",
		metric.WithDescription("Use case handler calls, by handler and outcome"), metric.WithUnit("{call}"))
	duration, _ = meter.Float64Histogram("frappe.usecase.duration",
		metric.WithDescription("Use case handler duration, by handler and outcome"), metric.WithUnit("s"))
)

// instrumentationScope is the scope of the handler metrics and spans. The
// span names carry the module themselves ("geo.query.get_country").
const instrumentationScope = "github.com/MathiasHilgert/frappe-api/internal/foundation/usecase"

// Observed wraps a Handler with a span, RED metrics and a structured log
// per call, all named after the wrapped handler's type.
type Observed[Input, Result any] struct {
	next     Handler[Input, Result]
	tracer   trace.Tracer
	name     string
	expected []error
}

// NewObserved wraps next. An error matching one of expected (errors.Is),
// such as a domain "not found", is an expected outcome: it is counted as
// "rejected" and logged at debug level, and it does not fail the span.
// Any other error fails the span and is logged at error level.
func NewObserved[Input, Result any](next Handler[Input, Result], expected ...error) *Observed[Input, Result] {
	return &Observed[Input, Result]{
		next:     next,
		tracer:   otel.Tracer(instrumentationScope),
		name:     handlerName{}.ofValue(next),
		expected: expected,
	}
}

// Name is the handler's telemetry name, for example geo.query.get_country.
func (observed *Observed[Input, Result]) Name() string {
	return observed.name
}

// Handle calls the wrapped handler inside a span named Name and records
// its outcome.
func (observed *Observed[Input, Result]) Handle(ctx context.Context, input Input) (Result, error) {
	start := time.Now()
	ctx, span := observed.tracer.Start(ctx, observed.name)
	defer span.End()

	result, err := observed.next.Handle(ctx, input)
	outcome := observed.outcome(err)
	attributes := metric.WithAttributes(attribute.String("handler", observed.name), attribute.String("outcome", outcome))
	calls.Add(ctx, 1, attributes)
	duration.Record(ctx, time.Since(start).Seconds(), attributes)

	logAttributes := []any{slog.String("handler", observed.name), slog.String("outcome", outcome), slog.Duration("duration", time.Since(start))}
	switch outcome {
	case outcomeError:
		span.RecordError(err)
		span.SetStatus(codes.Error, err.Error())
		slog.ErrorContext(ctx, "use case failed", append(logAttributes, slog.Any("error", err))...)
	case outcomeRejected:
		slog.DebugContext(ctx, "use case rejected", append(logAttributes, slog.Any("error", err))...)
	default:
		slog.DebugContext(ctx, "use case handled", logAttributes...)
	}
	return result, err
}

func (observed *Observed[Input, Result]) outcome(err error) string {
	if err == nil {
		return outcomeSuccess
	}
	for _, expected := range observed.expected {
		if errors.Is(err, expected) {
			return outcomeRejected
		}
	}
	return outcomeError
}
