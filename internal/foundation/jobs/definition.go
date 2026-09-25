package jobs

import (
	"context"
	"encoding/json"
	"fmt"
	"time"

	"github.com/robfig/cron/v3"
)

// minimumInterval is the shortest period Every accepts; backends schedule
// periodic jobs with second precision at best.
const minimumInterval = time.Second

// cronParser parses standard five field cron expressions and descriptors
// such as @hourly, evaluated in UTC.
var cronParser = cron.NewParser(cron.Minute | cron.Hour | cron.Dom | cron.Month | cron.Dow | cron.Descriptor)

// Definition is one job of a module carrying arguments of type Args. It is
// created once, as a package-level variable of the module that owns the job
// (jobs are private to their module), and used both to enqueue (Enqueue)
// and to handle (Handle) the job.
//
// Args must be small and JSON encodable: carry identifiers, never entities,
// and let the handler load current state, because a job can run more than
// once and long after it was enqueued. Handlers must be idempotent.
type Definition[Args any] struct {
	catalog *Catalog
	entry   *entry
}

// DefinitionOption customizes a Definition.
type DefinitionOption func(*entry)

// WithQueue sets the default queue of the job, a lowercase snake_case name.
func WithQueue(queue string) DefinitionOption {
	return func(definition *entry) { definition.queue = queue }
}

// WithMaxAttempts sets the maximum number of attempts, including the first.
func WithMaxAttempts(maximum int) DefinitionOption {
	return func(definition *entry) { definition.maxAttempts = maximum }
}

// WithTimeout bounds one attempt; the handler's ctx is cancelled after it.
func WithTimeout(timeout time.Duration) DefinitionOption {
	return func(definition *entry) { definition.timeout = timeout }
}

// Define declares the job "<module>.<action>" on module, where action is a
// lowercase snake_case verb phrase such as rebuild_index. It panics when
// action or an option is invalid, or when the name is already defined;
// both are programming errors caught at startup.
func Define[Args any](module *Module, action string, options ...DefinitionOption) *Definition[Args] {
	if !segmentPattern.MatchString(action) {
		panic(fmt.Sprintf("jobs: invalid action %q of module %q: must match %s", action, module.name, segmentPattern.String()))
	}
	definition := &entry{name: module.name + "." + action, queue: DefaultQueue}
	for _, option := range options {
		option(definition)
	}
	switch {
	case !segmentPattern.MatchString(definition.queue):
		panic(fmt.Sprintf("jobs: invalid queue %q of job %q: must match %s", definition.queue, definition.name, segmentPattern.String()))
	case definition.maxAttempts < 0:
		panic(fmt.Sprintf("jobs: max attempts of job %q must not be negative", definition.name))
	case definition.timeout < 0:
		panic(fmt.Sprintf("jobs: timeout of job %q must not be negative", definition.name))
	}
	module.catalog.define(definition)
	return &Definition[Args]{catalog: module.catalog, entry: definition}
}

// Name returns the job name, <module>.<action>.
func (definition *Definition[Args]) Name() string {
	return definition.entry.name
}

// Queue returns the default queue of the job.
func (definition *Definition[Args]) Queue() string {
	return definition.entry.queue
}

// Handler returns the Handler registered with Handle, and whether one is.
// Test kits use it to run a job synchronously; adapters use
// Catalog.Registrations instead.
func (definition *Definition[Args]) Handler() (Handler, bool) {
	definition.catalog.mutex.RLock()
	defer definition.catalog.mutex.RUnlock()
	handler := definition.entry.handler
	return handler, handler != nil
}

// Job is one execution attempt of a Definition, handed to its handler.
type Job[Args any] struct {
	// EnqueuedAt is when the job was inserted.
	EnqueuedAt time.Time
	// Args are the decoded job arguments.
	Args Args
	// Name is the job name, <module>.<action>.
	Name string
	// Queue is the queue the job runs in.
	Queue string
	// Tenant is the tenant that enqueued the job, empty when none.
	Tenant string
	// ID identifies the job.
	ID int64
	// Attempt is the attempt number, starting at 1.
	Attempt int
	// MaxAttempts is the maximum number of attempts.
	MaxAttempts int
}

// Handle registers handler for every job of definition. The registered
// Handler decodes the arguments, restores the enqueuing tenant, runs
// handler in a consumer span linked to the producer span, and records
// metrics and a structured log line. Arguments that cannot be decoded
// cancel the job. It panics when handler is nil or the job is already
// handled.
func Handle[Args any](definition *Definition[Args], handler func(ctx context.Context, job Job[Args]) error) {
	if handler == nil {
		panic(fmt.Sprintf("jobs: Handle of %q requires a handler", definition.entry.name))
	}
	definition.catalog.handle(definition.entry.name, typedHandler(definition, handler))
}

// EnqueueOption customizes one Enqueue.
type EnqueueOption func(*Request)

// After delays the job: it becomes available delay from now.
func After(delay time.Duration) EnqueueOption {
	return func(request *Request) { request.ScheduledAt = time.Now().Add(delay) }
}

// Queue overrides the queue of the job for this enqueue.
func Queue(queue string) EnqueueOption {
	return func(request *Request) { request.Queue = queue }
}

// Unique skips the insert when an equivalent job (same name and arguments)
// exists: within the same period bucket when period is positive, or while
// one has not finished yet when period is zero.
func Unique(period time.Duration) EnqueueOption {
	return func(request *Request) { request.Unique = &Uniqueness{Period: period} }
}

// Enqueue inserts a job carrying args. Inside a database transaction (see
// database.WithinTransaction) the job is inserted in that transaction, so
// it exists if and only if the transaction commits. It captures the trace
// context and the tenant of ctx, so the handler continues both.
func (definition *Definition[Args]) Enqueue(ctx context.Context, args Args, options ...EnqueueOption) (Receipt, error) {
	arguments, err := json.Marshal(args)
	if err != nil {
		return Receipt{}, fmt.Errorf("jobs: encode arguments of %q: %w", definition.entry.name, err)
	}
	request := Request{
		Name:        definition.entry.name,
		Queue:       definition.entry.queue,
		Arguments:   arguments,
		MaxAttempts: definition.entry.maxAttempts,
		Metadata:    map[string]string{},
	}
	for _, option := range options {
		option(&request)
	}
	if !segmentPattern.MatchString(request.Queue) {
		return Receipt{}, fmt.Errorf("jobs: invalid queue %q: must match %s", request.Queue, segmentPattern.String())
	}
	if resolve := definition.catalog.currentTenancy().Resolve; resolve != nil {
		if tenant, found := resolve(ctx); found && tenant != "" {
			request.Metadata[MetadataTenant] = tenant
		}
	}

	enqueuer, found := enqueuerFromContext(ctx)
	if !found {
		enqueuer = definition.catalog.currentEnqueuer()
	}
	if enqueuer == nil {
		return Receipt{}, ErrNoEnqueuer
	}
	return enqueueTraced(ctx, enqueuer, request)
}

// Every enqueues args on definition every interval (at least one second),
// once per tick across every replica. It panics on an invalid interval or
// a duplicate schedule.
func Every[Args any](definition *Definition[Args], interval time.Duration, args Args) {
	if interval < minimumInterval {
		panic(fmt.Sprintf("jobs: interval of %q must be at least %s, got %s", definition.entry.name, minimumInterval, interval))
	}
	addSchedule(definition, "every "+interval.String(), intervalTiming(interval), args)
}

// Cron enqueues args on definition on every tick of expression, a standard
// five field cron expression or descriptor (such as @hourly) in UTC, once
// per tick across every replica. It panics on an invalid expression or a
// duplicate schedule.
func Cron[Args any](definition *Definition[Args], expression string, args Args) {
	schedule, err := cronParser.Parse(expression)
	if err != nil {
		panic(fmt.Sprintf("jobs: invalid cron expression %q of %q: %v", expression, definition.entry.name, err))
	}
	addSchedule(definition, "cron "+expression, cronTiming{schedule: schedule}, args)
}

func addSchedule[Args any](definition *Definition[Args], timing string, next Timing, args Args) {
	arguments, err := json.Marshal(args)
	if err != nil {
		panic(fmt.Sprintf("jobs: encode schedule arguments of %q: %v", definition.entry.name, err))
	}
	definition.catalog.schedule(Schedule{
		Timing:      next,
		Identifier:  definition.entry.name + " " + timing + " " + string(arguments),
		Name:        definition.entry.name,
		Queue:       definition.entry.queue,
		Arguments:   arguments,
		MaxAttempts: definition.entry.maxAttempts,
	})
}

// intervalTiming ticks every fixed interval.
type intervalTiming time.Duration

func (interval intervalTiming) Next(current time.Time) time.Time {
	return current.Add(time.Duration(interval))
}

// cronTiming ticks on a cron schedule evaluated in UTC.
type cronTiming struct {
	schedule cron.Schedule
}

func (timing cronTiming) Next(current time.Time) time.Time {
	return timing.schedule.Next(current.UTC())
}
