package machinetranslation

import (
	"cmp"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"log/slog"
	"slices"
	"time"

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
	outcomeTooLarge        = "failed_too_large"
	outcomeRejected        = "failed_rejected"
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
// failure skips what is already current. Texts it still works on are
// leased (their pending rows' requested_at moves past the next attempt),
// so neither reads nor the expired sweep request them again meanwhile.
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
	run := translation{machine: machine, texts: texts, tenant: job.Tenant, target: target}
	now := machine.settings.Now()
	if until, paused := machine.circuit.paused(now); paused {
		machine.fail(ctx, "paused")
		return run.snooze(ctx, referenced(job.Args), until.Sub(now))
	}
	var batches []batch
	err = machine.transactor(ctx, job.Tenant, func(ctx context.Context) error {
		var planError error
		batches, planError = run.plan(ctx, job.Args)
		return planError
	})
	if err != nil {
		return fmt.Errorf("machinetranslation: load texts: %w", err)
	}
	for index, planned := range batches {
		if err := run.translate(ctx, planned); err != nil {
			var remaining []item
			for _, left := range batches[index:] {
				remaining = append(remaining, left.items...)
			}
			return run.failure(ctx, err, planned.items, remaining, job)
		}
	}
	return nil
}

// translation is one job run: its tenant, target and texts.
type translation struct {
	texts   Texts
	machine *MachineTranslation
	target  i18n.Locale
	tenant  string
}

// within runs work in a transaction of the run's tenant.
func (run translation) within(ctx context.Context, work func(ctx context.Context) error) error {
	return run.machine.transactor(ctx, run.tenant, work)
}

// referenced returns the job's texts, for leasing them.
func referenced(args TranslateArguments) []item {
	items := make([]item, 0, len(args.Texts))
	for _, reference := range args.Texts {
		if id, err := localizedtext.ParseID(reference.ID); err == nil {
			items = append(items, item{id: id, hash: reference.SourceHash})
		}
	}
	return items
}

// lease keeps items pending, unrequested, until until.
func (run translation) lease(ctx context.Context, items []item, until time.Time) error {
	return run.within(ctx, func(ctx context.Context) error {
		return run.texts.LeasePending(ctx, run.target, ids(items), until)
	})
}

// snooze leases items past wait and snoozes the job for wait.
func (run translation) snooze(ctx context.Context, items []item, wait time.Duration) error {
	if err := run.lease(ctx, items, run.machine.settings.Now().Add(wait+leaseMargin)); err != nil {
		return fmt.Errorf("machinetranslation: lease texts: %w", err)
	}
	return jobs.Snooze(wait)
}

// markFailed gives items up until their source changes.
func (run translation) markFailed(ctx context.Context, items []item, outcome string) error {
	err := run.within(ctx, func(ctx context.Context) error {
		return run.texts.MarkFailed(ctx, run.target, ids(items))
	})
	if err != nil {
		return fmt.Errorf("machinetranslation: mark texts failed: %w", err)
	}
	for range items {
		run.machine.count(ctx, outcome)
	}
	return nil
}

func ids(items []item) []localizedtext.ID {
	converted := make([]localizedtext.ID, len(items))
	for index, planned := range items {
		converted[index] = planned.id
	}
	return converted
}

// plan loads the texts, leases those still needing a machine translation
// for this attempt and groups them by source locale and context (the
// text's own, else the one requested, the Field default) into batches
// under the request size budget. A text over the budget alone is marked
// failed.
func (run translation) plan(ctx context.Context, args TranslateArguments) ([]batch, error) {
	groups, order, leased, err := run.group(ctx, args)
	if err != nil {
		return nil, err
	}
	until := run.machine.settings.Now().Add(translateTimeout + leaseMargin)
	if err := run.texts.LeasePending(ctx, run.target, ids(leased), until); err != nil {
		return nil, err
	}
	var batches []batch
	var oversized []item
	for _, key := range order {
		split, tooLarge := splitBySize(*groups[key], run.machine.settings.MaxRequestBytes, run.machine.settings.BatchSize)
		batches = append(batches, split...)
		oversized = append(oversized, tooLarge...)
	}
	if len(oversized) > 0 {
		if err := run.texts.MarkFailed(ctx, run.target, ids(oversized)); err != nil {
			return nil, err
		}
		for range oversized {
			run.machine.count(ctx, outcomeTooLarge)
		}
	}
	return batches, nil
}

// groupKey groups texts sharing a source locale and context.
type groupKey struct {
	source  string
	context string
}

// group loads the texts that still need a translation and groups them.
func (run translation) group(ctx context.Context, args TranslateArguments) (map[groupKey]*batch, []groupKey, []item, error) {
	groups := map[groupKey]*batch{}
	var order []groupKey
	var loaded []item
	for _, reference := range args.Texts {
		text, outcome, err := run.machine.load(ctx, run.texts, run.target, reference)
		if err != nil {
			return nil, nil, nil, err
		}
		if outcome != "" {
			run.machine.count(ctx, outcome)
			continue
		}
		key := groupKey{source: text.SourceLocale.String(), context: cmp.Or(text.Context, args.Context)}
		if groups[key] == nil {
			groups[key] = &batch{request: Request{Source: text.SourceLocale, Target: run.target, Context: key.context}}
			order = append(order, key)
		}
		planned := item{id: text.ID, hash: text.SourceHash}
		groups[key].request.Texts = append(groups[key].request.Texts, text.SourceValue)
		groups[key].items = append(groups[key].items, planned)
		loaded = append(loaded, planned)
	}
	return groups, order, loaded, nil
}

// splitBySize splits whole into batches of at most count texts whose JSON
// encoded texts and context fit maximumBytes, and returns apart the items
// whose text alone does not fit.
func splitBySize(whole batch, maximumBytes, count int) ([]batch, []item) {
	budget := maximumBytes - requestOverhead - encodedSize(whole.request.Context)
	empty := func() batch {
		return batch{request: Request{Source: whole.request.Source, Target: whole.request.Target, Context: whole.request.Context}}
	}
	var batches []batch
	var oversized []item
	current := empty()
	used := 0
	for index, text := range whole.request.Texts {
		size := encodedSize(text) + 1
		if size > budget {
			oversized = append(oversized, whole.items[index])
			continue
		}
		if len(current.items) > 0 && (used+size > budget || len(current.items) >= count) {
			batches = append(batches, current)
			current, used = empty(), 0
		}
		current.request.Texts = append(current.request.Texts, text)
		current.items = append(current.items, whole.items[index])
		used += size
	}
	if len(current.items) > 0 {
		batches = append(batches, current)
	}
	return batches, oversized
}

// encodedSize is the length of value as a JSON string.
func encodedSize(value string) int {
	encoded, err := json.Marshal(value)
	if err != nil {
		return len(value)
	}
	return len(encoded)
}

// translate translates and stores one batch. A batch the provider finds
// too large is split in halves; a single text still too large is marked
// failed.
func (run translation) translate(ctx context.Context, planned batch) error {
	translated, err := run.machine.call(ctx, planned.request)
	if errors.Is(err, ErrPayloadTooLarge) {
		if len(planned.items) == 1 {
			return run.markFailed(ctx, planned.items, outcomeTooLarge)
		}
		half := len(planned.items) / 2
		for _, part := range []batch{slice(planned, 0, half), slice(planned, half, len(planned.items))} {
			if partError := run.translate(ctx, part); partError != nil {
				return partError
			}
		}
		return nil
	}
	if err != nil {
		return err
	}
	run.machine.circuit.succeeded(ctx)
	err = run.within(ctx, func(ctx context.Context) error {
		return run.machine.store(ctx, run.texts, run.target, planned.items, translated)
	})
	if err != nil {
		return fmt.Errorf("machinetranslation: store translations: %w", err)
	}
	return nil
}

// slice returns the texts from to to of whole.
func slice(whole batch, from, to int) batch {
	request := whole.request
	request.Texts = whole.request.Texts[from:to]
	return batch{request: request, items: whole.items[from:to]}
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

// failure maps a provider error to the job outcome. A rate limit snoozes
// for Retry-After. An exhausted quota or rejected credentials pause every
// translation of the process (the circuit) and snooze until the pause
// ends. A permanent rejection marks the failing batch failed and cancels.
// Anything else retries with backoff. Remaining texts stay leased past
// the next attempt; after the last attempt the lease lapses and the
// expired sweep takes over.
func (run translation) failure(ctx context.Context, err error, failing, remaining []item, job jobs.Job[TranslateArguments]) error {
	machine := run.machine
	now := machine.settings.Now()
	var limited RateLimitedError
	var permanent PermanentError
	switch {
	case errors.As(err, &limited):
		machine.fail(ctx, "rate_limited")
		return run.snooze(ctx, remaining, limited.RetryAfter)
	case errors.Is(err, ErrQuotaExceeded), errors.Is(err, ErrUnauthorized):
		reason := "quota_exceeded"
		if errors.Is(err, ErrUnauthorized) {
			reason = "unauthorized"
		}
		machine.fail(ctx, reason)
		machine.circuit.open(ctx, now, now.Add(machine.settings.QuotaPause), reason, err)
		until, _ := machine.circuit.paused(now)
		return run.snooze(ctx, remaining, until.Sub(now))
	case errors.As(err, &permanent):
		machine.fail(ctx, "permanent")
		machine.logger.ErrorContext(ctx, "machine translation rejected permanently, texts marked failed", slog.Any("error", err))
		if markError := run.markFailed(ctx, failing, outcomeRejected); markError != nil {
			return markError
		}
		return jobs.Cancel(err)
	default:
		machine.fail(ctx, "transient")
		if job.Attempt < job.MaxAttempts {
			if leaseError := run.lease(ctx, remaining, now.Add(retryLease(job.Attempt))); leaseError != nil {
				return errors.Join(err, leaseError)
			}
		}
		return err
	}
}

// retryLease bounds the wait before the next attempt after a failed
// attempt: River retries after attempt^4 seconds plus up to 10% jitter.
func retryLease(attempt int) time.Duration {
	backoff := time.Duration(attempt*attempt*attempt*attempt) * time.Second
	return backoff + backoff/5 + translateTimeout + leaseMargin
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
	slices.Sort(tenants)
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
