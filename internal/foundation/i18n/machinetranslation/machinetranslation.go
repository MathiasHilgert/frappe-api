package machinetranslation

import (
	"cmp"
	"context"
	"errors"
	"fmt"
	"log/slog"
	"slices"
	"time"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/metric"
	"go.opentelemetry.io/otel/trace"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs"
)

// Defaults for zero Settings fields.
const (
	DefaultBatchSize            = 50
	DefaultExpiredSweepInterval = 5 * time.Minute
	DefaultOrphanSweepInterval  = time.Hour
	DefaultOrphanMinimumAge     = 24 * time.Hour
	DefaultSweepLimit           = 500
)

// Queue is the queue translation jobs run in, apart from other jobs so a
// provider outage never starves them.
const Queue = "machine_translation"

const (
	instrumentationName  = "github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/machinetranslation"
	translateMaxAttempts = 10
	translateTimeout     = 2 * time.Minute
	sweepTimeout         = 5 * time.Minute
)

var (
	errDisabled = errors.New("machinetranslation: machine translation is disabled (no translator configured)")
	errNoTenant = errors.New("machinetranslation: job carries no tenant; global texts are not machine translated")
)

// Settings configures machine translation.
type Settings struct {
	// Translator is the provider. Nil disables machine translation:
	// Requester returns nil (nothing is requested or marked pending) and
	// translation jobs still queued are cancelled.
	Translator Translator
	// Transactor opens tenant scoped transactions. Required.
	Transactor Transactor
	// Logger defaults to slog.Default().
	Logger *slog.Logger
	// BatchSize is the maximum number of texts per job and provider
	// request. Zero means DefaultBatchSize.
	BatchSize int
	// ExpiredSweepInterval is how often lost requests are requested
	// again. Zero means DefaultExpiredSweepInterval.
	ExpiredSweepInterval time.Duration
	// OrphanSweepInterval is how often unreferenced texts are deleted.
	// Zero means DefaultOrphanSweepInterval.
	OrphanSweepInterval time.Duration
	// OrphanMinimumAge leaves room for transactions that created a text
	// and have not committed its referencing row yet. Zero means
	// DefaultOrphanMinimumAge.
	OrphanMinimumAge time.Duration
	// SweepLimit bounds the rows one sweep handles per tenant. Zero means
	// DefaultSweepLimit.
	SweepLimit int
}

// TextReference is one text to translate, with the source hash it was
// requested for.
type TextReference struct {
	ID         string `json:"id"`
	SourceHash string `json:"sourceHash"`
}

// TranslateArguments are the arguments of the translate job: texts of one
// tenant (the job's), into one locale, requested with one context.
type TranslateArguments struct {
	Locale  string          `json:"locale"`
	Context string          `json:"context,omitempty"`
	Texts   []TextReference `json:"texts"`
}

// SweepArguments are the (empty) arguments of the periodic sweeps.
type SweepArguments struct{}

// MachineTranslation owns the translation jobs and the periodic sweeps.
type MachineTranslation struct {
	translator     Translator
	transactor     Transactor
	logger         *slog.Logger
	translate      *jobs.Definition[TranslateArguments]
	requestExpired *jobs.Definition[SweepArguments]
	deleteOrphans  *jobs.Definition[SweepArguments]
	tracer         trace.Tracer
	translations   metric.Int64Counter
	failures       metric.Int64Counter
	swept          metric.Int64Counter
	settings       Settings
}

// New validates settings and defines, on module, the jobs
// <module>.machine_translate, <module>.request_expired_translations and
// <module>.delete_orphan_texts. Call Register once the localized text
// Service exists (it needs Requester first).
func New(module *jobs.Module, settings Settings) (*MachineTranslation, error) {
	if settings.Transactor == nil {
		return nil, errors.New("machinetranslation: Settings.Transactor is required")
	}
	applyDefaults(&settings)
	meter := otel.Meter(instrumentationName)
	machine := &MachineTranslation{
		translator: settings.Translator,
		transactor: settings.Transactor,
		logger:     settings.Logger,
		settings:   settings,
		tracer:     otel.Tracer(instrumentationName),
		translate: jobs.Define[TranslateArguments](module, "machine_translate",
			jobs.WithQueue(Queue), jobs.WithMaxAttempts(translateMaxAttempts), jobs.WithTimeout(translateTimeout)),
		requestExpired: jobs.Define[SweepArguments](module, "request_expired_translations", jobs.WithTimeout(sweepTimeout)),
		deleteOrphans:  jobs.Define[SweepArguments](module, "delete_orphan_texts", jobs.WithTimeout(sweepTimeout)),
	}
	var errs [3]error
	machine.translations, errs[0] = meter.Int64Counter("frappe.machinetranslation.translations",
		metric.WithDescription("Requested machine translations by outcome (stored, discarded because the source changed, skipped)."))
	machine.failures, errs[1] = meter.Int64Counter("frappe.machinetranslation.failures",
		metric.WithDescription("Translation job failures by kind (rate_limited, quota_exceeded, permanent, transient, no_tenant, disabled)."))
	machine.swept, errs[2] = meter.Int64Counter("frappe.machinetranslation.swept",
		metric.WithDescription("Rows handled by the periodic sweeps, by sweep (expired, orphans)."))
	if err := errors.Join(errs[:]...); err != nil {
		return nil, fmt.Errorf("machinetranslation: create metrics: %w", err)
	}
	return machine, nil
}

func applyDefaults(settings *Settings) {
	if settings.Logger == nil {
		settings.Logger = slog.Default()
	}
	settings.BatchSize = cmp.Or(max(settings.BatchSize, 0), DefaultBatchSize)
	settings.ExpiredSweepInterval = cmp.Or(max(settings.ExpiredSweepInterval, 0), DefaultExpiredSweepInterval)
	settings.OrphanSweepInterval = cmp.Or(max(settings.OrphanSweepInterval, 0), DefaultOrphanSweepInterval)
	settings.OrphanMinimumAge = cmp.Or(max(settings.OrphanMinimumAge, 0), DefaultOrphanMinimumAge)
	settings.SweepLimit = cmp.Or(max(settings.SweepLimit, 0), DefaultSweepLimit)
}

// TranslateJob returns the translate job definition (tests run it).
func (machine *MachineTranslation) TranslateJob() *jobs.Definition[TranslateArguments] {
	return machine.translate
}

// RequestExpiredJob returns the expired request sweep definition.
func (machine *MachineTranslation) RequestExpiredJob() *jobs.Definition[SweepArguments] {
	return machine.requestExpired
}

// DeleteOrphansJob returns the orphan sweep definition.
func (machine *MachineTranslation) DeleteOrphansJob() *jobs.Definition[SweepArguments] {
	return machine.deleteOrphans
}

// Requester returns the localizedtext.TranslationRequester to hand to the
// localized text Service, or nil while disabled: the Service then
// requests nothing and marks nothing pending, and reads fall back to the
// source.
func (machine *MachineTranslation) Requester() localizedtext.TranslationRequester {
	if machine.translator == nil {
		return nil
	}
	return requester{definition: machine.translate, batchSize: machine.settings.BatchSize}
}

// Register handles the jobs on module (the one given to New) with texts
// and schedules the sweeps. The orphan sweep runs even while machine
// translation is disabled.
func (machine *MachineTranslation) Register(module *jobs.Module, texts Texts) {
	jobs.Handle(module, machine.translate, func(ctx context.Context, job jobs.Job[TranslateArguments]) error {
		return machine.handleTranslate(ctx, texts, job)
	})
	jobs.Handle(module, machine.requestExpired, func(ctx context.Context, _ jobs.Job[SweepArguments]) error {
		return machine.sweepExpired(ctx, texts)
	})
	jobs.Handle(module, machine.deleteOrphans, func(ctx context.Context, _ jobs.Job[SweepArguments]) error {
		return machine.sweepOrphans(ctx, texts)
	})
	jobs.Every(machine.requestExpired, machine.settings.ExpiredSweepInterval, SweepArguments{})
	jobs.Every(machine.deleteOrphans, machine.settings.OrphanSweepInterval, SweepArguments{})
}

// requester enqueues one translate job per locale, context and batch, in
// the caller's transaction: the jobs exist if and only if the change that
// needed them commits. The jobs Tenancy captures the tenant.
//
// Requests are deduplicated by the pending rows, not by River unique
// jobs: the Service hands over only requests it just recorded as pending,
// and a pending translation is not requested again until it expires. A
// River unique job would instead drop a new request (a source changed
// again, a translation deleted) while an equal job is still running or
// not yet marked completed, after it already loaded its texts.
type requester struct {
	definition *jobs.Definition[TranslateArguments]
	batchSize  int
}

type batchKey struct {
	locale  string
	context string
}

// RequestTranslations implements localizedtext.TranslationRequester.
func (requester requester) RequestTranslations(ctx context.Context, requests []localizedtext.TranslationRequest) error {
	groups := map[batchKey][]TextReference{}
	for _, request := range requests {
		key := batchKey{locale: request.Locale.String(), context: request.Context}
		groups[key] = append(groups[key], TextReference{ID: request.TextID.String(), SourceHash: request.SourceHash})
	}
	keys := make([]batchKey, 0, len(groups))
	for key := range groups {
		keys = append(keys, key)
	}
	// Sorted, so batches are deterministic.
	slices.SortFunc(keys, func(left, right batchKey) int {
		return cmp.Or(cmp.Compare(left.locale, right.locale), cmp.Compare(left.context, right.context))
	})
	for _, key := range keys {
		references := groups[key]
		slices.SortFunc(references, func(left, right TextReference) int { return cmp.Compare(left.ID, right.ID) })
		for batch := range slices.Chunk(references, requester.batchSize) {
			args := TranslateArguments{Locale: key.locale, Context: key.context, Texts: batch}
			if _, err := requester.definition.Enqueue(ctx, args); err != nil {
				return fmt.Errorf("machinetranslation: enqueue translation: %w", err)
			}
		}
	}
	return nil
}
