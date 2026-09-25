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
	// UpdateSource replaces the source fields and Context of text.ID,
	// marks every non-pending translation whose SourceHash differs from
	// text.SourceHash stale, deletes a translation into the new source
	// locale, and returns the updated text with all its translations. It
	// returns ErrNotFound for an unknown text.
	UpdateSource(ctx context.Context, text Text) (Text, error)
	// SetManualTranslation upserts a current manual translation of the
	// text's current source.
	SetManualTranslation(ctx context.Context, id ID, locale i18n.Locale, value string) error
	// SetMachineTranslation upserts a current machine translation unless
	// the text's source hash is no longer sourceHash or a manual
	// translation exists, and reports whether it was stored.
	SetMachineTranslation(ctx context.Context, id ID, locale i18n.Locale, value, sourceHash string) (bool, error)
	// MarkPending records requested machine translations: it inserts a
	// pending row for a missing translation and turns a stale machine
	// translation pending (keeping its value). It never touches a manual
	// or current translation, nor a text of another tenant (or a global
	// text). It returns the requests it recorded, which the Service then
	// hands to the TranslationRequester, so a request is never repeated
	// while pending.
	MarkPending(ctx context.Context, requests []TranslationRequest) ([]TranslationRequest, error)
	// Get returns a text with all its translations, or ErrNotFound.
	Get(ctx context.Context, id ID) (Text, error)
	// LoadForLocale returns the texts among ids that exist, each with its
	// translation into locale if any, in one round trip.
	LoadForLocale(ctx context.Context, ids []ID, locale i18n.Locale) ([]Text, error)
	// Delete deletes texts and, by cascade, their translations, and
	// returns how many texts it deleted.
	Delete(ctx context.Context, ids []ID) (int64, error)
	// DeleteOrphans deletes up to limit texts last updated more than
	// olderThan ago that no column of fields references, and returns how
	// many it deleted.
	DeleteOrphans(ctx context.Context, fields []Field, olderThan time.Duration, limit int) (int64, error)
}

// TranslationRequester is the "translation needed" hook: the Service
// calls it, inside the caller's transaction, whenever a text needs a
// machine translation (new or changed source, or a missing or stale
// machine translation found on read). It must not translate inline; the
// machine translation feature appends the requests to the outbox and
// translates asynchronously, then stores results with
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
