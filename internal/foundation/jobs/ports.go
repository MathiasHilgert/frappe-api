package jobs

import (
	"context"
	"errors"
	"fmt"
	"time"
)

// DefaultQueue is the queue a definition uses unless WithQueue says otherwise.
const DefaultQueue = "default"

// Metadata keys the typed layer writes on enqueue and reads on execution.
// Adapters must carry Request.Metadata to Delivery.Metadata unchanged.
const (
	// MetadataTraceParent is the W3C traceparent of the producer span.
	MetadataTraceParent = "traceparent"
	// MetadataTraceState is the W3C tracestate accompanying the traceparent.
	MetadataTraceState = "tracestate"
	// MetadataTenant is the tenant captured from the enqueuing context.
	MetadataTenant = "tenant"
)

// ErrNoEnqueuer is returned by Definition.Enqueue before the composition
// root installed an Enqueuer on the catalog with Catalog.Use.
var ErrNoEnqueuer = errors.New("jobs: no enqueuer configured; the composition root must call Catalog.Use")

// Uniqueness asks the backend to skip inserting a job when an equivalent
// job (same name, same encoded arguments) already exists.
type Uniqueness struct {
	// Period scopes uniqueness to a time bucket of this length, e.g. one
	// job per hour. Zero means unique while an equivalent job has not
	// finished yet (it is pending, scheduled, available, running or
	// retryable).
	Period time.Duration
}

// Request is one job to insert, built by Definition.Enqueue.
type Request struct {
	// ScheduledAt is when the job becomes available. Zero means now.
	ScheduledAt time.Time
	// Unique, when not nil, deduplicates the job.
	Unique *Uniqueness
	// Metadata carries trace context and tenant; adapters must preserve it.
	Metadata map[string]string
	// Name is the job name, <module>.<action>.
	Name string
	// Queue is the queue the job is inserted in.
	Queue string
	// Arguments are the JSON encoded job arguments.
	Arguments []byte
	// MaxAttempts is the maximum number of attempts, including the first.
	// Zero leaves it to the backend default.
	MaxAttempts int
}

// Receipt reports the outcome of an Enqueue.
type Receipt struct {
	// ID identifies the job, or the existing equivalent job when Duplicate.
	ID int64
	// Duplicate is true when Uniqueness skipped the insert.
	Duplicate bool
}

// Enqueuer inserts jobs. The production implementation (jobs/river) joins
// the database transaction found in ctx, so a job enqueued inside a unit of
// work is inserted if and only if that unit of work commits.
type Enqueuer interface {
	Enqueue(ctx context.Context, request Request) (Receipt, error)
}

// Delivery is one execution attempt of a job, handed by an adapter to the
// Handler registered for its name.
type Delivery struct {
	// EnqueuedAt is when the job was inserted.
	EnqueuedAt time.Time
	// ScheduledAt is when the job became available; lag is measured from it.
	ScheduledAt time.Time
	// Metadata is Request.Metadata as it was enqueued.
	Metadata map[string]string
	// Name is the job name, <module>.<action>.
	Name string
	// Queue is the queue the job runs in.
	Queue string
	// Arguments are the JSON encoded job arguments.
	Arguments []byte
	// ID identifies the job.
	ID int64
	// Attempt is the attempt number, starting at 1.
	Attempt int
	// MaxAttempts is the maximum number of attempts.
	MaxAttempts int
}

// Handler executes one delivery. Returning nil completes the job; an error
// retries it with backoff until MaxAttempts; an error built with Cancel
// stops it for good; one built with Snooze reschedules it without
// consuming an attempt.
type Handler func(ctx context.Context, delivery Delivery) error

// Registration is the handler of one job name, with its execution settings.
type Registration struct {
	// Handler executes deliveries of Name.
	Handler Handler
	// Name is the job name, <module>.<action>.
	Name string
	// Queue is the default queue of the job.
	Queue string
	// Timeout bounds one attempt. Zero leaves it to the backend default.
	Timeout time.Duration
	// MaxAttempts is the maximum number of attempts. Zero leaves it to the
	// backend default.
	MaxAttempts int
}

// Timing computes the next run of a periodic schedule.
type Timing interface {
	// Next returns the first run strictly after current.
	Next(current time.Time) time.Time
}

// Schedule is one periodic enqueue of a job, registered with Every or Cron.
// Backends must enqueue each tick once across every replica.
type Schedule struct {
	// Timing computes the ticks.
	Timing Timing
	// Identifier uniquely and stably identifies the schedule.
	Identifier string
	// Name is the job name, <module>.<action>.
	Name string
	// Queue is the queue ticks are enqueued in.
	Queue string
	// Arguments are the JSON encoded arguments of every tick.
	Arguments []byte
	// MaxAttempts is the maximum number of attempts of every tick.
	MaxAttempts int
}

// cancelError marks a handler error that must not be retried.
type cancelError struct {
	cause error
}

func (cancel cancelError) Error() string {
	if cancel.cause == nil {
		return "jobs: cancelled"
	}
	return "jobs: cancelled: " + cancel.cause.Error()
}

func (cancel cancelError) Unwrap() error {
	return cancel.cause
}

// Cancel wraps err so the job stops for good, whatever attempts remain,
// e.g. when the entity it works on no longer exists. Cancel(nil) still
// cancels.
func Cancel(err error) error {
	return cancelError{cause: err}
}

// IsCancel reports whether err, or any error it wraps, was built with Cancel.
func IsCancel(err error) bool {
	var cancel cancelError
	return errors.As(err, &cancel)
}

// snoozeError asks for the job to run again later.
type snoozeError struct {
	duration time.Duration
}

func (snooze snoozeError) Error() string {
	return fmt.Sprintf("jobs: snoozed for %s", snooze.duration)
}

// Snooze reschedules the job to run again after duration without consuming
// an attempt, e.g. while waiting for an external resource. It panics when
// duration is negative.
func Snooze(duration time.Duration) error {
	if duration < 0 {
		panic(fmt.Sprintf("jobs: snooze duration must not be negative, got %s", duration))
	}
	return snoozeError{duration: duration}
}

// SnoozeDuration reports the duration of an error built with Snooze.
func SnoozeDuration(err error) (time.Duration, bool) {
	var snooze snoozeError
	if errors.As(err, &snooze) {
		return snooze.duration, true
	}
	return 0, false
}
