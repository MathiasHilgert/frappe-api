package machinetranslation

import (
	"cmp"
	"context"
	"errors"
	"fmt"
	"log/slog"
	"slices"

	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/codes"
	"go.opentelemetry.io/otel/metric"
	"go.opentelemetry.io/otel/trace"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs"
)

// Translation outcomes, the outcome attribute of
// frappe.machinetranslation.translations.
const (
	outcomeStored          = "stored"
	outcomeDiscarded       = "discarded_source_changed"
	outcomeManual          = "skipped_manual"
	outcomeCurrent         = "skipped_current"
	outcomeDeleted         = "skipped_deleted"
	outcomeOutdatedRequest = "skipped_outdated_request"
	outcomeEmpty           = "skipped_empty_result"
)

// item is one text to translate, as loaded.
type item struct {
	hash string
	id   localizedtext.ID
}

// batch is one provider request with the texts it translates.
type batch struct {
	request Request
	items   []item
}

// handleTranslate translates the job's texts. It loads them in one tenant
// transaction, calls the provider outside any transaction, and stores
// each batch in its own tenant transaction, so a retry after a partial
// failure skips what is already current.
func (machine *MachineTranslation) handleTranslate(ctx context.Context, texts Texts, job jobs.Job[TranslateArguments]) error {
	if machine.translator == nil {
		machine.fail(ctx, "disabled")
		return jobs.Cancel(errDisabled)
	}
	if job.Tenant == "" {
		machine.fail(ctx, "no_tenant")
		machine.logger.ErrorContext(ctx, "machine translation job without a tenant cancelled", slog.Int64("job_id", job.ID))
		return jobs.Cancel(errNoTenant)
	}
	target, err := i18n.ParseLocale(job.Args.Locale)
	if err != nil {
		return jobs.Cancel(fmt.Errorf("machinetranslation: job locale: %w", err))
	}
	var batches []batch
	err = machine.transactor(ctx, job.Tenant, func(ctx context.Context) error {
		var planError error
		batches, planError = machine.plan(ctx, texts, target, job.Args)
		return planError
	})
	if err != nil {
		return fmt.Errorf("machinetranslation: load texts: %w", err)
	}
	for _, planned := range batches {
		translated, err := machine.call(ctx, planned.request)
		if err != nil {
			return machine.failure(ctx, err)
		}
		err = machine.transactor(ctx, job.Tenant, func(ctx context.Context) error {
			return machine.store(ctx, texts, target, planned.items, translated)
		})
		if err != nil {
			return fmt.Errorf("machinetranslation: store translations: %w", err)
		}
	}
	return nil
}

// plan loads the texts and groups those still needing a machine
// translation by source locale and context (the text's own, else the one
// requested, the Field default).
func (machine *MachineTranslation) plan(ctx context.Context, texts Texts, target i18n.Locale, args TranslateArguments) ([]batch, error) {
	type groupKey struct {
		source  string
		context string
	}
	groups := map[groupKey]*batch{}
	var order []groupKey
	for _, reference := range args.Texts {
		text, outcome, err := machine.load(ctx, texts, target, reference)
		if err != nil {
			return nil, err
		}
		if outcome != "" {
			machine.count(ctx, outcome)
			continue
		}
		key := groupKey{source: text.SourceLocale.String(), context: cmp.Or(text.Context, args.Context)}
		if groups[key] == nil {
			groups[key] = &batch{request: Request{Source: text.SourceLocale, Target: target, Context: key.context}}
			order = append(order, key)
		}
		groups[key].request.Texts = append(groups[key].request.Texts, text.SourceValue)
		groups[key].items = append(groups[key].items, item{id: text.ID, hash: text.SourceHash})
	}
	batches := make([]batch, 0, len(order))
	for _, key := range order {
		batches = append(batches, *groups[key])
	}
	return batches, nil
}

// load returns the text, or the outcome explaining why it needs no
// translation any more.
func (*MachineTranslation) load(ctx context.Context, texts Texts, target i18n.Locale, reference TextReference) (localizedtext.Text, string, error) {
	id, err := localizedtext.ParseID(reference.ID)
	if err != nil {
		return localizedtext.Text{}, outcomeDeleted, nil //nolint:nilerr // a malformed id names no text.
	}
	text, err := texts.Get(ctx, id)
	if errors.Is(err, localizedtext.ErrNotFound) {
		return localizedtext.Text{}, outcomeDeleted, nil
	}
	if err != nil {
		return localizedtext.Text{}, "", err
	}
	return text, skipReason(text, target, reference), nil
}

// skipReason says why text needs no machine translation into target any
// more, or "" when it does.
func skipReason(text localizedtext.Text, target i18n.Locale, reference TextReference) string {
	if text.SourceHash != reference.SourceHash || text.SourceLocale == target {
		// The source changed after the request; the change requested the
		// new source itself.
		return outcomeOutdatedRequest
	}
	translation, found := text.Translation(target)
	switch {
	case found && translation.Origin == localizedtext.OriginManual:
		return outcomeManual
	case found && translation.Status == localizedtext.StatusCurrent && translation.SourceHash == text.SourceHash:
		return outcomeCurrent
	default:
		return ""
	}
}

// call runs one provider request in a span.
func (machine *MachineTranslation) call(ctx context.Context, request Request) ([]string, error) {
	ctx, span := machine.tracer.Start(ctx, "machinetranslation translate", trace.WithAttributes(
		attribute.String("machinetranslation.source", request.Source.String()),
		attribute.String("machinetranslation.target", request.Target.String()),
		attribute.Int("machinetranslation.texts", len(request.Texts))))
	defer span.End()
	translated, err := machine.translator.Translate(ctx, request)
	if err == nil && len(translated) != len(request.Texts) {
		err = fmt.Errorf("machinetranslation: provider returned %d translations for %d texts", len(translated), len(request.Texts))
	}
	if err != nil {
		span.RecordError(err)
		span.SetStatus(codes.Error, "translation failed")
	}
	return translated, err
}

// store saves each translation for the hash it was made from; the store
// discards it when the source changed meanwhile or a manual one exists.
func (machine *MachineTranslation) store(ctx context.Context, texts Texts, target i18n.Locale, items []item, translated []string) error {
	for index, planned := range items {
		stored, err := texts.SetMachineTranslation(ctx, planned.id, target, translated[index], planned.hash)
		switch {
		case errors.Is(err, localizedtext.ErrEmptyValue):
			machine.count(ctx, outcomeEmpty)
		case errors.Is(err, localizedtext.ErrNotFound):
			machine.count(ctx, outcomeDeleted)
		case err != nil:
			return err
		case stored:
			machine.count(ctx, outcomeStored)
		default:
			machine.count(ctx, outcomeDiscarded)
		}
	}
	return nil
}

// failure maps a provider error to the job outcome: a rate limit snoozes,
// an exhausted quota or a permanent rejection cancels (the expired sweep
// requests the texts again later), anything else retries with backoff.
func (machine *MachineTranslation) failure(ctx context.Context, err error) error {
	var limited RateLimitedError
	var permanent PermanentError
	switch {
	case errors.As(err, &limited):
		machine.fail(ctx, "rate_limited")
		return jobs.Snooze(limited.RetryAfter)
	case errors.Is(err, ErrQuotaExceeded):
		machine.fail(ctx, "quota_exceeded")
		machine.logger.ErrorContext(ctx, "machine translation quota exhausted, job cancelled; pending translations are requested again by the expired sweep",
			slog.Any("error", err))
		return jobs.Cancel(err)
	case errors.As(err, &permanent):
		machine.fail(ctx, "permanent")
		machine.logger.ErrorContext(ctx, "machine translation rejected permanently, job cancelled", slog.Any("error", err))
		return jobs.Cancel(err)
	default:
		machine.fail(ctx, "transient")
		return err
	}
}

func (machine *MachineTranslation) count(ctx context.Context, outcome string) {
	machine.translations.Add(ctx, 1, metric.WithAttributes(attribute.String("outcome", outcome)))
}

func (machine *MachineTranslation) fail(ctx context.Context, kind string) {
	machine.failures.Add(ctx, 1, metric.WithAttributes(attribute.String("kind", kind)))
}

// sweepExpired requests again, per tenant, the pending translations whose
// request was lost. A failing tenant does not stop the others.
func (machine *MachineTranslation) sweepExpired(ctx context.Context, texts Texts) error {
	return machine.perTenant(ctx, texts, "expired", func(ctx context.Context) (int64, error) {
		requested, err := texts.RequestExpired(ctx, machine.settings.SweepLimit)
		return int64(requested), err
	})
}

// sweepOrphans deletes, per tenant, texts nothing references. Without any
// declared Field there is nothing to sweep; a schema that does not match
// the declared Fields cancels the tick (the next one checks again).
func (machine *MachineTranslation) sweepOrphans(ctx context.Context, texts Texts) error {
	err := machine.perTenant(ctx, texts, "orphans", func(ctx context.Context) (int64, error) {
		return texts.DeleteOrphans(ctx, machine.settings.OrphanMinimumAge, machine.settings.SweepLimit)
	})
	switch {
	case errors.Is(err, localizedtext.ErrNoFields):
		return nil
	case errors.Is(err, localizedtext.ErrUndeclaredReference), errors.Is(err, localizedtext.ErrMissingForeignKey),
		errors.Is(err, localizedtext.ErrUnsafeForeignKey):
		machine.logger.ErrorContext(ctx, "orphan text sweep refused: localized text foreign keys do not match the declared fields", slog.Any("error", err))
		return jobs.Cancel(err)
	default:
		return err
	}
}

func (machine *MachineTranslation) perTenant(ctx context.Context, texts Texts, sweep string, work func(ctx context.Context) (int64, error)) error {
	var tenants []string
	err := machine.transactor(ctx, "", func(ctx context.Context) error {
		var listError error
		tenants, listError = texts.Tenants(ctx)
		return listError
	})
	if err != nil {
		return fmt.Errorf("machinetranslation: list tenants: %w", err)
	}
	var failures []error
	for _, tenant := range slices.Compact(tenants) {
		var handled int64
		err := machine.transactor(ctx, tenant, func(ctx context.Context) error {
			var workError error
			handled, workError = work(ctx)
			return workError
		})
		if err != nil {
			failures = append(failures, fmt.Errorf("tenant %s: %w", tenant, err))
			continue
		}
		machine.swept.Add(ctx, handled, metric.WithAttributes(attribute.String("sweep", sweep)))
	}
	return errors.Join(failures...)
}
