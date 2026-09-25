package localizedtext

import (
	"context"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
)

// Store persists localized texts. Every method runs in the caller's
// transaction, whose tenant setting scopes it (see the postgres package).
type Store interface {
	// Create inserts text (ID, source fields and Context).
	Create(ctx context.Context, text Text) error
	// UpdateSource replaces the source fields of text.ID (and its Context
	// when source.Context is set or source.ClearContext), marks every
	// non-pending translation whose SourceHash differs from the new hash
	// stale, deletes a translation into the new source locale, and returns
	// the updated text with all its translations. It returns ErrNotFound
	// for an unknown text and ErrReadOnlyText for a global one.
	UpdateSource(ctx context.Context, id ID, source Source, hash string) (Text, error)
	// SetManualTranslation upserts a current manual translation of the
	// text's current source, share-locking the text so a concurrent
	// source change is either seen or waits. It returns ErrNotFound or
	// ErrReadOnlyText.
	SetManualTranslation(ctx context.Context, id ID, locale i18n.Locale, value string) error
	// SetMachineTranslation upserts a current machine translation unless
	// the text's source hash is no longer sourceHash or a manual
	// translation exists, and reports whether it was stored.
	SetMachineTranslation(ctx context.Context, id ID, locale i18n.Locale, value, sourceHash string) (bool, error)
	// MarkPending records requested machine translations with the text's
	// current source hash, a request time and one more attempt: it
	// inserts a pending row for a missing translation, turns a stale
	// machine translation pending (keeping its value) and re-marks a
	// pending one requested more than pendingTimeout ago. It never touches
	// a manual or current translation, nor a text of another tenant (or a
	// global text). It returns the requests it recorded, which the Service
	// then hands to the TranslationRequester, so a request is never
	// repeated while pending.
	MarkPending(ctx context.Context, requests []TranslationRequest, pendingTimeout time.Duration) ([]TranslationRequest, error)
	// ExpiredPending returns up to limit of the tenant's pending machine
	// translations requested more than olderThan ago, oldest first, with
	// the text's own Context (empty means the Field default).
	ExpiredPending(ctx context.Context, olderThan time.Duration, limit int) ([]TranslationRequest, error)
	// Get returns a text with all its translations, or ErrNotFound.
	Get(ctx context.Context, id ID) (Text, error)
	// LoadForLocale returns the texts among ids that exist, each with its
	// translation into locale if any, in one round trip.
	LoadForLocale(ctx context.Context, ids []ID, locale i18n.Locale) ([]Text, error)
	// Delete deletes texts and, by cascade, their translations, and
	// returns how many texts it deleted.
	Delete(ctx context.Context, ids []ID) (int64, error)
	// References returns every foreign key to localized_texts (id) outside
	// the localized text tables themselves.
	References(ctx context.Context) ([]Reference, error)
	// DeleteOrphans deletes up to limit texts last updated more than
	// olderThan ago that no reference points at, and returns how many it
	// deleted.
	DeleteOrphans(ctx context.Context, references []Reference, olderThan time.Duration, limit int) (int64, error)
	// Isolate runs work so that its failure, including a database error,
	// leaves the caller's transaction usable (a savepoint).
	Isolate(ctx context.Context, work func(ctx context.Context) error) error
}

// TranslationRequester is the "translation needed" hook: the Service
// calls it, inside the caller's transaction, whenever a text needs a
// machine translation (new or changed source, or a missing, stale or
// expired pending machine translation found on read). It must not
// translate inline; the machine translation feature appends the requests
// to the outbox and translates asynchronously, then stores results with
// Service.SetMachineTranslation.
type TranslationRequester interface {
	RequestTranslations(ctx context.Context, requests []TranslationRequest) error
}

// LocaleResolver is the subset of *i18n.Catalog the Service needs.
type LocaleResolver interface {
	Source() i18n.Locale
	Supported() []i18n.Locale
	Resolve(locale i18n.Locale) i18n.Locale
}
