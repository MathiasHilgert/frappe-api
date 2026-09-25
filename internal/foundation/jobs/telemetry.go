package jobs

import (
	"context"
	"encoding/json"
	"fmt"
	"log/slog"
	"strconv"
	"sync"
	"time"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/codes"
	"go.opentelemetry.io/otel/metric"
	"go.opentelemetry.io/otel/propagation"
	"go.opentelemetry.io/otel/trace"
)

// InstrumentationScope names the tracer and meter of the jobs platform;
// adapters use it too so every jobs instrument shares one scope.
const InstrumentationScope = "github.com/MathiasHilgert/frappe-api/internal/foundation/jobs"

// Outcomes recorded on frappe.jobs.handled and frappe.jobs.handle.duration.
const (
	outcomeSuccess   = "success"
	outcomeFailure   = "failure"
	outcomeCancelled = "cancelled"
	outcomeSnoozed   = "snoozed"
	outcomeInvalid   = "invalid"
)

type instruments struct {
	tracer   trace.Tracer
	enqueued metric.Int64Counter
	handled  metric.Int64Counter
	duration metric.Float64Histogram
	lag      metric.Float64Histogram
	attempts metric.Int64Histogram
}

// telemetry holds the tracer and instruments, created lazily through the
// global OpenTelemetry API so they follow whatever providers the telemetry
// foundation installs.
var telemetry = sync.OnceValue(func() instruments {
	meter := otel.Meter(InstrumentationScope)
	enqueued, err := meter.Int64Counter("frappe.jobs.enqueued",
		metric.WithDescription("Jobs enqueued, by name, queue and outcome."),
		metric.WithUnit("{job}"))
	must(err)
	handled, err := meter.Int64Counter("frappe.jobs.handled",
		metric.WithDescription("Job attempts handled, by name, queue and outcome."),
		metric.WithUnit("{job}"))
	must(err)
	duration, err := meter.Float64Histogram("frappe.jobs.handle.duration",
		metric.WithDescription("Duration of job attempts, by name, queue and outcome."),
		metric.WithUnit("s"))
	must(err)
	lag, err := meter.Float64Histogram("frappe.jobs.lag",
		metric.WithDescription("Delay between a job becoming available and its attempt starting, by name and queue."),
		metric.WithUnit("s"))
	must(err)
	attempts, err := meter.Int64Histogram("frappe.jobs.attempts",
		metric.WithDescription("Attempt number of every job attempt, by name and queue."),
		metric.WithUnit("{attempt}"),
		metric.WithExplicitBucketBoundaries(1, 2, 3, 5, 10, 25))
	must(err)
	return instruments{
		tracer:   otel.Tracer(InstrumentationScope),
		enqueued: enqueued,
		handled:  handled,
		duration: duration,
		lag:      lag,
		attempts: attempts,
	}
})

func must(err error) {
	if err != nil {
		panic(fmt.Errorf("jobs: create instrument: %w", err))
	}
}

// enqueueTraced enqueues request in a producer span whose context is carried
// in the request metadata, and records frappe.jobs.enqueued.
func enqueueTraced(ctx context.Context, enqueuer Enqueuer, request Request) (Receipt, error) {
	telemetry := telemetry()
	ctx, span := telemetry.tracer.Start(ctx, request.Name+" enqueue",
		trace.WithSpanKind(trace.SpanKindProducer),
		trace.WithAttributes(
			attribute.String("messaging.system", "jobs"),
			attribute.String("messaging.operation.type", "send"),
			attribute.String("messaging.destination.name", request.Queue),
			attribute.String("jobs.name", request.Name),
		))
	defer span.End()

	carrier := propagation.MapCarrier{}
	propagation.TraceContext{}.Inject(ctx, carrier)
	for key, value := range carrier {
		request.Metadata[key] = value
	}

	outcome := outcomeSuccess
	receipt, err := enqueuer.Enqueue(ctx, request)
	switch {
	case err != nil:
		outcome = outcomeFailure
		span.RecordError(err)
		span.SetStatus(codes.Error, err.Error())
	case receipt.Duplicate:
		outcome = "duplicate"
	}
	if err == nil {
		span.SetAttributes(attribute.String("messaging.message.id", strconv.FormatInt(receipt.ID, 10)))
	}
	telemetry.enqueued.Add(ctx, 1, metric.WithAttributes(
		attribute.String("name", request.Name),
		attribute.String("queue", request.Queue),
		attribute.String("outcome", outcome),
	))
	if err != nil {
		return Receipt{}, fmt.Errorf("jobs: enqueue %q: %w", request.Name, err)
	}
	return receipt, nil
}

// typedHandler adapts a typed handler to a Handler with tenant restoration,
// tracing, metrics and logging.
func typedHandler[Args any](definition *Definition[Args], handler func(context.Context, Job[Args]) error) Handler {
	catalog := definition.catalog
	return func(ctx context.Context, delivery Delivery) (err error) {
		telemetry := telemetry()
		start := time.Now()
		tenant := delivery.Metadata[MetadataTenant]

		producer := trace.SpanContextFromContext(propagation.TraceContext{}.Extract(context.Background(), propagation.MapCarrier{
			MetadataTraceParent: delivery.Metadata[MetadataTraceParent],
			MetadataTraceState:  delivery.Metadata[MetadataTraceState],
		}))
		spanOptions := []trace.SpanStartOption{
			trace.WithSpanKind(trace.SpanKindConsumer),
			trace.WithAttributes(
				attribute.String("messaging.system", "jobs"),
				attribute.String("messaging.operation.type", "process"),
				attribute.String("messaging.destination.name", delivery.Queue),
				attribute.String("messaging.message.id", strconv.FormatInt(delivery.ID, 10)),
				attribute.String("jobs.name", delivery.Name),
				attribute.Int("jobs.attempt", delivery.Attempt),
			),
		}
		if producer.IsValid() {
			spanOptions = append(spanOptions, trace.WithLinks(trace.Link{SpanContext: producer}))
		}
		ctx, span := telemetry.tracer.Start(ctx, delivery.Name+" process", spanOptions...)
		defer span.End()

		if bind := catalog.currentTenancy().Bind; bind != nil && tenant != "" {
			ctx = bind(ctx, tenant)
		}

		var args Args
		var outcome string
		if decodeErr := json.Unmarshal(delivery.Arguments, &args); decodeErr != nil {
			outcome, err = outcomeInvalid, Cancel(fmt.Errorf("jobs: decode arguments of %q: %w", delivery.Name, decodeErr))
		} else {
			err = runRecovered(ctx, handler, Job[Args]{
				EnqueuedAt:  delivery.EnqueuedAt,
				Args:        args,
				Name:        delivery.Name,
				Queue:       delivery.Queue,
				Tenant:      tenant,
				ID:          delivery.ID,
				Attempt:     delivery.Attempt,
				MaxAttempts: delivery.MaxAttempts,
			})
			outcome = outcomeOf(err)
		}
		if err != nil && outcome != outcomeSnoozed {
			span.RecordError(err)
			span.SetStatus(codes.Error, err.Error())
		}

		elapsed := time.Since(start)
		nameAndQueue := make([]attribute.KeyValue, 0, 3)
		nameAndQueue = append(nameAndQueue, attribute.String("name", delivery.Name), attribute.String("queue", delivery.Queue))
		withOutcome := metric.WithAttributes(append(nameAndQueue, attribute.String("outcome", outcome))...)
		telemetry.handled.Add(ctx, 1, withOutcome)
		telemetry.duration.Record(ctx, elapsed.Seconds(), withOutcome)
		telemetry.attempts.Record(ctx, int64(delivery.Attempt), metric.WithAttributes(nameAndQueue...))
		if !delivery.ScheduledAt.IsZero() {
			telemetry.lag.Record(ctx, max(start.Sub(delivery.ScheduledAt), 0).Seconds(), metric.WithAttributes(nameAndQueue...))
		}
		logOutcome(ctx, delivery, tenant, outcome, elapsed, err)
		return err
	}
}

// runRecovered runs handler, turning a panic into a retryable error so one
// bad job never takes the worker down.
func runRecovered[Args any](ctx context.Context, handler func(context.Context, Job[Args]) error, job Job[Args]) (err error) {
	defer func() {
		if recovered := recover(); recovered != nil {
			err = fmt.Errorf("jobs: handler of %q panicked: %v", job.Name, recovered)
		}
	}()
	return handler(ctx, job)
}

func outcomeOf(err error) string {
	switch {
	case err == nil:
		return outcomeSuccess
	case IsCancel(err):
		return outcomeCancelled
	default:
		if _, snoozed := SnoozeDuration(err); snoozed {
			return outcomeSnoozed
		}
		return outcomeFailure
	}
}

// logOutcome writes one structured line per attempt: debug on success and
// snooze, warn on a retryable failure, error on cancellation.
func logOutcome(ctx context.Context, delivery Delivery, tenant, outcome string, elapsed time.Duration, err error) {
	level := slog.LevelDebug
	switch outcome {
	case outcomeFailure:
		level = slog.LevelWarn
	case outcomeCancelled, outcomeInvalid:
		level = slog.LevelError
	}
	attributes := []slog.Attr{
		slog.Int64("job_id", delivery.ID),
		slog.String("job_name", delivery.Name),
		slog.String("job_queue", delivery.Queue),
		slog.Int("job_attempt", delivery.Attempt),
		slog.Int("job_max_attempts", delivery.MaxAttempts),
		slog.String("tenant", tenant),
		slog.String("outcome", outcome),
		slog.Duration("duration", elapsed),
	}
	if err != nil {
		attributes = append(attributes, slog.Any("error", err))
	}
	slog.LogAttrs(ctx, level, "jobs: attempt finished", attributes...)
}
