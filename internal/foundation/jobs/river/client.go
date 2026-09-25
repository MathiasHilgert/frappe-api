// Package river is the jobs backend on River (github.com/riverqueue/river),
// a Postgres job queue. It implements jobs.Enqueuer and works every handler
// of a jobs.Catalog; modules never import it.
//
// Enqueue joins the database transaction found in ctx
// (database.TransactionFromContext), so a job enqueued inside a unit of work
// is inserted if and only if that unit of work commits; outside one it
// inserts on the pool. The client runs as the application role: River's
// tables carry no tenant data beyond the job metadata and have no Row Level
// Security, and the schema comes from migrations/20260926000000_river.sql.
//
// Periodic schedules (jobs.Every, jobs.Cron) are enqueued by the one
// replica River elects leader, so each tick is enqueued once whatever the
// number of replicas.
//
// Besides the metrics the jobs package records per job, the client records
// frappe.jobs.periodic.ticks, and samples frappe.jobs.leader (1 on the
// elected replica, 0 elsewhere, no attributes: series are told apart by the
// service.instance.id resource attribute) and frappe.jobs.queue.depth (by
// queue and state) every Settings.MetricsInterval. Queue depth is a
// property of the shared database, so only the leader reports it: summing
// the gauge across replicas never counts a job twice.
package river

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"log/slog"
	"sync"
	"time"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"
	riverqueue "github.com/riverqueue/river"
	"github.com/riverqueue/river/riverdriver/riverpgxv5"
	"github.com/riverqueue/river/rivertype"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/metric"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs"
)

// ErrUnknownQueue is returned by Enqueue for a queue no definition of the
// catalog uses: no client would ever work a job inserted there.
var ErrUnknownQueue = errors.New("river jobs: queue is not configured")

// Defaults applied to zero Settings fields.
const (
	DefaultWorkers           = 10
	DefaultFetchPollInterval = time.Second
	DefaultMetricsInterval   = 15 * time.Second
)

// Settings configures a Client. Zero values select River's or this
// package's defaults.
type Settings struct {
	// MeterProvider records the backend metrics. Nil uses the global
	// provider installed by the telemetry foundation.
	MeterProvider metric.MeterProvider
	// FetchPollInterval is how often each queue polls for jobs when no
	// notification arrives.
	FetchPollInterval time.Duration
	// JobTimeout bounds an attempt of a job whose definition sets none.
	JobTimeout time.Duration
	// CompletedRetention is how long completed jobs are kept.
	CompletedRetention time.Duration
	// MetricsInterval is how often queue depth and leadership are sampled.
	MetricsInterval time.Duration
	// Workers is the maximum number of concurrent attempts per queue.
	Workers int
	// MaxAttempts applies to jobs whose definition sets none.
	MaxAttempts int
}

// Client is the River jobs backend. It is safe for concurrent use.
type Client struct {
	registration metric.Registration
	instruments  clientInstruments
	client       *riverqueue.Client[pgxTransaction]
	pool         *pgxpool.Pool
	queues       map[string]struct{}
	cancel       context.CancelFunc
	done         chan struct{}
	identifier   string
	sample       snapshot
	settings     Settings
	mutex        sync.Mutex
	working      bool
}

// Statically assert Client satisfies jobs.Enqueuer.
var _ jobs.Enqueuer = (*Client)(nil)

// New validates catalog and builds a client working its handlers and
// schedules on pool, which must connect as the application role. It does
// not start working; call Start. Enqueue works before Start.
func New(pool *pgxpool.Pool, catalog *jobs.Catalog, settings Settings) (*Client, error) {
	if err := catalog.Validate(); err != nil {
		return nil, err
	}
	settings = withDefaults(settings)
	instruments, err := newClientInstruments(settings.MeterProvider)
	if err != nil {
		return nil, err
	}
	identifier := "frappe-" + uuid.NewString()
	queues := map[string]struct{}{jobs.DefaultQueue: {}}
	for _, queue := range catalog.Queues() {
		queues[queue] = struct{}{}
	}

	registrations := catalog.Registrations()
	config := &riverqueue.Config{
		ID:                          identifier,
		FetchPollInterval:           settings.FetchPollInterval,
		FetchCooldown:               min(settings.FetchPollInterval, 100*time.Millisecond),
		JobTimeout:                  settings.JobTimeout,
		MaxAttempts:                 settings.MaxAttempts,
		CompletedJobRetentionPeriod: settings.CompletedRetention,
		Logger:                      slog.Default(),
	}
	working := len(registrations) > 0
	if working {
		workers := riverqueue.NewWorkers()
		for _, registration := range registrations {
			riverqueue.AddWorkerArgs(workers, arguments{kind: registration.Name}, &worker{registration: registration})
		}
		config.Workers = workers
		config.Queues = map[string]riverqueue.QueueConfig{}
		for queue := range queues {
			config.Queues[queue] = riverqueue.QueueConfig{MaxWorkers: settings.Workers}
		}
		for _, schedule := range catalog.Schedules() {
			config.PeriodicJobs = append(config.PeriodicJobs, periodicJob(schedule, instruments.ticks))
		}
	}

	client, err := riverqueue.NewClient(riverpgxv5.New(pool), config)
	if err != nil {
		return nil, fmt.Errorf("river jobs: new client: %w", err)
	}
	return &Client{
		instruments: instruments,
		client:      client,
		pool:        pool,
		queues:      queues,
		identifier:  identifier,
		settings:    settings,
		working:     working,
	}, nil
}

func withDefaults(settings Settings) Settings {
	if settings.Workers <= 0 {
		settings.Workers = DefaultWorkers
	}
	if settings.FetchPollInterval <= 0 {
		settings.FetchPollInterval = DefaultFetchPollInterval
	}
	if settings.MetricsInterval <= 0 {
		settings.MetricsInterval = DefaultMetricsInterval
	}
	if settings.MeterProvider == nil {
		settings.MeterProvider = otel.GetMeterProvider()
	}
	return settings
}

// unfinishedStates scopes jobs.Unique to jobs that have not finished, so an
// equivalent job may be enqueued again once the previous one completed,
// was cancelled or discarded.
var unfinishedStates = []rivertype.JobState{
	rivertype.JobStateAvailable,
	rivertype.JobStatePending,
	rivertype.JobStateRetryable,
	rivertype.JobStateRunning,
	rivertype.JobStateScheduled,
}

// Enqueue implements jobs.Enqueuer.
func (client *Client) Enqueue(ctx context.Context, request jobs.Request) (jobs.Receipt, error) {
	if _, configured := client.queues[request.Queue]; !configured {
		return jobs.Receipt{}, fmt.Errorf("%w: %q", ErrUnknownQueue, request.Queue)
	}
	metadata, err := json.Marshal(request.Metadata)
	if err != nil {
		return jobs.Receipt{}, fmt.Errorf("river jobs: encode metadata: %w", err)
	}
	options := &riverqueue.InsertOpts{
		Queue:       request.Queue,
		ScheduledAt: request.ScheduledAt,
		MaxAttempts: request.MaxAttempts,
		Metadata:    metadata,
	}
	if request.Unique != nil {
		options.UniqueOpts = riverqueue.UniqueOpts{ByArgs: true, ByPeriod: request.Unique.Period, ByState: unfinishedStates}
	}
	args := arguments{kind: request.Name, encoded: request.Arguments}

	var result *rivertype.JobInsertResult
	if transaction, ok := database.TransactionFromContext(ctx); ok {
		result, err = client.client.InsertTx(ctx, transaction, args, options)
	} else {
		result, err = client.client.Insert(ctx, args, options)
	}
	if err != nil {
		return jobs.Receipt{}, fmt.Errorf("river jobs: insert: %w", err)
	}
	return jobs.Receipt{ID: result.Job.ID, Duplicate: result.UniqueSkippedAsDuplicate}, nil
}

// Start starts working jobs, electing a leader for periodic schedules, and
// starts sampling queue depth and leadership. A catalog without handlers
// only samples metrics.
func (client *Client) Start(ctx context.Context) error {
	client.mutex.Lock()
	defer client.mutex.Unlock()
	if client.done != nil {
		return errors.New("river jobs: client already started")
	}
	if client.working {
		// ctx only bounds starting: the application lifecycle cancels its
		// Up phase context as soon as Up returns, and River ties its
		// fetch, election and maintenance loops to the context it starts
		// with. Stop and StopAndCancel end them instead.
		if err := client.client.Start(context.WithoutCancel(ctx)); err != nil {
			return fmt.Errorf("river jobs: start: %w", err)
		}
	}
	registration, err := client.registerGauges()
	if err != nil {
		if client.working {
			_ = client.client.Stop(ctx)
		}
		return err
	}
	client.registration = registration
	samplerCtx, cancel := context.WithCancel(context.WithoutCancel(ctx))
	client.cancel = cancel
	client.done = make(chan struct{})
	go client.runSampler(samplerCtx, client.done)
	return nil
}

// Stop stops fetching, waits for running attempts until ctx ends (then
// cancels them, so they are retried elsewhere) and stops sampling.
func (client *Client) Stop(ctx context.Context) error {
	client.mutex.Lock()
	defer client.mutex.Unlock()
	if client.done == nil {
		return nil
	}
	client.cancel()
	<-client.done
	client.done = nil
	var stopErr error
	if client.working {
		if err := client.client.Stop(ctx); err != nil {
			stopErr = errors.Join(err, client.client.StopAndCancel(context.WithoutCancel(ctx)))
		}
	}
	return errors.Join(stopErr, client.registration.Unregister())
}

// Check reports whether the job tables are reachable.
func (client *Client) Check(ctx context.Context) error {
	var exists bool
	if err := client.pool.QueryRow(ctx, "SELECT EXISTS (SELECT 1 FROM river_job LIMIT 1)").Scan(&exists); err != nil {
		return fmt.Errorf("river jobs: check: %w", err)
	}
	return nil
}

// pgxTransaction is the transaction type of the riverpgxv5 driver.
type pgxTransaction = pgx.Tx

// arguments carries already encoded job arguments under a dynamic kind, so
// one Go type serves every job name.
type arguments struct {
	kind    string
	encoded json.RawMessage
}

// Kind implements riverqueue.JobArgs.
func (args arguments) Kind() string {
	return args.kind
}

// MarshalJSON returns the encoded arguments as they are.
func (args arguments) MarshalJSON() ([]byte, error) {
	if len(args.encoded) == 0 {
		return []byte("{}"), nil
	}
	return args.encoded, nil
}

// UnmarshalJSON keeps the encoded arguments as they are.
func (args *arguments) UnmarshalJSON(data []byte) error {
	args.encoded = append(json.RawMessage(nil), data...)
	return nil
}

// worker hands River jobs to one jobs.Registration.
type worker struct {
	riverqueue.WorkerDefaults[arguments]
	registration jobs.Registration
}

// Timeout implements riverqueue.Worker; zero applies Settings.JobTimeout.
func (worker *worker) Timeout(*riverqueue.Job[arguments]) time.Duration {
	return worker.registration.Timeout
}

// Work implements riverqueue.Worker, translating jobs.Cancel and
// jobs.Snooze into River's cancel and snooze.
func (worker *worker) Work(ctx context.Context, job *riverqueue.Job[arguments]) error {
	err := worker.registration.Handler(ctx, delivery(job.JobRow))
	if err == nil {
		return nil
	}
	if duration, snoozed := jobs.SnoozeDuration(err); snoozed {
		return riverqueue.JobSnooze(duration)
	}
	if jobs.IsCancel(err) {
		return riverqueue.JobCancel(err)
	}
	return err
}

// delivery translates a River job row into a jobs.Delivery. Metadata keeps
// only string values; River may add its own keys of other types.
func delivery(row *rivertype.JobRow) jobs.Delivery {
	metadata := map[string]string{}
	var decoded map[string]any
	if err := json.Unmarshal(row.Metadata, &decoded); err == nil {
		for key, value := range decoded {
			if text, ok := value.(string); ok {
				metadata[key] = text
			}
		}
	}
	return jobs.Delivery{
		EnqueuedAt:  row.CreatedAt,
		ScheduledAt: row.ScheduledAt,
		Metadata:    metadata,
		Name:        row.Kind,
		Queue:       row.Queue,
		Arguments:   row.EncodedArgs,
		ID:          row.ID,
		Attempt:     row.Attempt,
		MaxAttempts: row.MaxAttempts,
	}
}

// periodicJob adapts a jobs.Schedule, counting frappe.jobs.periodic.ticks
// every time the leader enqueues it.
func periodicJob(schedule jobs.Schedule, ticks metric.Int64Counter) *riverqueue.PeriodicJob {
	digest := sha256.Sum256([]byte(schedule.Identifier))
	identifier := schedule.Name + "." + hex.EncodeToString(digest[:8])
	metadata, _ := json.Marshal(map[string]string{"schedule": schedule.Identifier})
	attributes := metric.WithAttributes(attribute.String("name", schedule.Name), attribute.String("queue", schedule.Queue))
	return riverqueue.NewPeriodicJob(schedule.Timing, func() (riverqueue.JobArgs, *riverqueue.InsertOpts) {
		ticks.Add(context.Background(), 1, attributes)
		return arguments{kind: schedule.Name, encoded: schedule.Arguments}, &riverqueue.InsertOpts{
			Queue:       schedule.Queue,
			MaxAttempts: schedule.MaxAttempts,
			Metadata:    metadata,
		}
	}, &riverqueue.PeriodicJobOpts{ID: identifier})
}

// clientInstruments are the instruments only a backend can record.
type clientInstruments struct {
	ticks  metric.Int64Counter
	depth  metric.Int64ObservableGauge
	leader metric.Int64ObservableGauge
	meter  metric.Meter
}

// newClientInstruments creates the backend instruments on provider.
func newClientInstruments(provider metric.MeterProvider) (clientInstruments, error) {
	meter := provider.Meter(jobs.InstrumentationScope)
	ticks, err := meter.Int64Counter("frappe.jobs.periodic.ticks",
		metric.WithDescription("Periodic schedule ticks enqueued by the leader, by name and queue."),
		metric.WithUnit("{tick}"))
	if err != nil {
		return clientInstruments{}, fmt.Errorf("river jobs: create ticks counter: %w", err)
	}
	depth, err := meter.Int64ObservableGauge("frappe.jobs.queue.depth",
		metric.WithDescription("Unfinished jobs, by queue and state, reported by the leader only."),
		metric.WithUnit("{job}"))
	if err != nil {
		return clientInstruments{}, fmt.Errorf("river jobs: create queue depth gauge: %w", err)
	}
	leader, err := meter.Int64ObservableGauge("frappe.jobs.leader",
		metric.WithDescription("1 on the replica elected leader (which enqueues periodic jobs), 0 elsewhere."),
		metric.WithUnit("1"))
	if err != nil {
		return clientInstruments{}, fmt.Errorf("river jobs: create leader gauge: %w", err)
	}
	return clientInstruments{ticks: ticks, depth: depth, leader: leader, meter: meter}, nil
}
