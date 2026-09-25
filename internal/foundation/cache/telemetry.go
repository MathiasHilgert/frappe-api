package cache

import (
	"context"
	"fmt"
	"log/slog"
	"sync"
	"time"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/metric"
)

// instrumentationScope names the meter used by the cache.
const instrumentationScope = "github.com/MathiasHilgert/frappe-api/internal/foundation/cache"

// errorLogInterval bounds how often cache failures are logged, so an
// unavailable store does not turn every read into a log line.
const errorLogInterval = 30 * time.Second

// Outcomes recorded on frappe.cache.requests.
const (
	outcomeHit   = "hit"
	outcomeMiss  = "miss"
	outcomeError = "error"
)

// Operations and reasons recorded on frappe.cache.errors.
const (
	operationKey    = "key"
	operationGet    = "get"
	operationSet    = "set"
	operationDelete = "delete"
	operationDecode = "decode"
	operationEncode = "encode"

	reasonMissingTenant  = "missing_tenant"
	reasonInvalidKey     = "invalid_key"
	reasonStoreError     = "store_error"
	reasonCorruptedEntry = "corrupted_entry"
	reasonEncodeError    = "encode_error"
)

type instruments struct {
	requests     metric.Int64Counter
	errors       metric.Int64Counter
	loadDuration metric.Float64Histogram
}

// cacheTelemetry creates the instruments lazily through the global
// OpenTelemetry API, so they follow whatever provider telemetry installs.
// Attributes are bounded: the module (first segment of the entry name),
// outcome, operation and reason; never a tenant or a key.
var cacheTelemetry = sync.OnceValue(func() instruments {
	meter := otel.Meter(instrumentationScope)
	requests, err := meter.Int64Counter("frappe.cache.requests",
		metric.WithDescription("Cache reads, by module and outcome (hit, miss, error)."),
		metric.WithUnit("{request}"))
	if err != nil {
		panic(fmt.Errorf("cache: create requests counter: %w", err))
	}
	failures, err := meter.Int64Counter("frappe.cache.errors",
		metric.WithDescription("Cache failures that were bypassed (fail open or closed), by operation and reason."),
		metric.WithUnit("{error}"))
	if err != nil {
		panic(fmt.Errorf("cache: create errors counter: %w", err))
	}
	loadDuration, err := meter.Float64Histogram("frappe.cache.load.duration",
		metric.WithDescription("Duration of loads behind the cache, by module."),
		metric.WithUnit("s"))
	if err != nil {
		panic(fmt.Errorf("cache: create load duration histogram: %w", err))
	}
	return instruments{requests: requests, errors: failures, loadDuration: loadDuration}
})

func recordRequest(ctx context.Context, module, outcome string) {
	cacheTelemetry().requests.Add(ctx, 1, metric.WithAttributes(
		attribute.String("module", module), attribute.String("outcome", outcome)))
}

func recordLoad(ctx context.Context, module string, started time.Time) {
	cacheTelemetry().loadDuration.Record(ctx, time.Since(started).Seconds(),
		metric.WithAttributes(attribute.String("module", module)))
}

// failure records one bypassed failure and logs it, throttled.
func (backend *Backend) failure(ctx context.Context, entry, operation, reason string, err error) {
	cacheTelemetry().errors.Add(ctx, 1, metric.WithAttributes(
		attribute.String("operation", operation), attribute.String("reason", reason)))
	backend.errorLog.warn(ctx, entry, operation, reason, err)
}

// throttledLog logs a warning at most once per interval, reporting how
// many occurrences were suppressed in between.
type throttledLog struct {
	last       time.Time
	logger     *slog.Logger
	interval   time.Duration
	suppressed int
	mutex      sync.Mutex
}

func (log *throttledLog) warn(ctx context.Context, entry, operation, reason string, err error) {
	log.mutex.Lock()
	now := time.Now()
	if !log.last.IsZero() && now.Sub(log.last) < log.interval {
		log.suppressed++
		log.mutex.Unlock()
		return
	}
	suppressed := log.suppressed
	log.last = now
	log.suppressed = 0
	log.mutex.Unlock()

	log.logger.WarnContext(ctx, "cache bypassed, loading from the source",
		slog.String("entry", entry),
		slog.String("operation", operation),
		slog.String("reason", reason),
		slog.Any("error", err),
		slog.Int("suppressed_since_last_warning", suppressed),
	)
}
