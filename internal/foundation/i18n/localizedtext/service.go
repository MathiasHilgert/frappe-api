package localizedtext

import (
	"context"
	"errors"
	"fmt"
	"slices"
	"strings"
	"sync"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
)

// Settings configures a Service.
type Settings struct {
	// Store persists texts. Required.
	Store Store
	// Locales resolves requested locales; pass the *i18n.Catalog.
	// Required.
	Locales LocaleResolver
	// Requester receives translation requests. Optional: without it,
	// nothing is requested and nothing is marked pending.
	Requester TranslationRequester
}

// Service is the localized text API. Build one per application and hand
// each module the FieldTexts of the fields it declares.
type Service struct {
	store     Store
	locales   LocaleResolver
	requester TranslationRequester
	fields    map[[2]string]Field
	mutex     sync.Mutex
}

// NewService validates settings and returns a Service.
func NewService(settings Settings) (*Service, error) {
	if settings.Store == nil {
		return nil, errors.New("localizedtext: Settings.Store is required")
	}
	if settings.Locales == nil {
		return nil, errors.New("localizedtext: Settings.Locales is required")
	}
	return &Service{
		store:     settings.Store,
		locales:   settings.Locales,
		requester: settings.Requester,
		fields:    map[[2]string]Field{},
	}, nil
}

// Field declares field and returns the API for its texts. Declaring the
// same field again is idempotent; declaring it with another Context
// fails. Declare every referencing column at startup: DeleteOrphans only
// keeps texts a declared column references.
func (service *Service) Field(field Field) (*FieldTexts, error) {
	if err := field.validate(); err != nil {
		return nil, err
	}
	key := [2]string{field.Table, field.Column}
	service.mutex.Lock()
	defer service.mutex.Unlock()
	if declared, ok := service.fields[key]; ok && declared != field {
		return nil, fmt.Errorf("%w: %s.%s already declared with context %q", ErrInvalidField, field.Table, field.Column, declared.Context)
	}
	service.fields[key] = field
	return &FieldTexts{service: service, field: field}, nil
}

// Fields returns every declared field, sorted by table and column.
func (service *Service) Fields() []Field {
	service.mutex.Lock()
	defer service.mutex.Unlock()
	fields := make([]Field, 0, len(service.fields))
	for _, field := range service.fields {
		fields = append(fields, field)
	}
	slices.SortFunc(fields, func(left, right Field) int {
		return strings.Compare(left.Table+"."+left.Column, right.Table+"."+right.Column)
	})
	return fields
}

// Get returns a text with all its translations.
func (service *Service) Get(ctx context.Context, id ID) (Text, error) {
	return service.store.Get(ctx, id)
}

// SetManualTranslation stores a person's translation of the current
// source. It is never overwritten by a machine translation; when the
// source changes it is only marked stale.
func (service *Service) SetManualTranslation(ctx context.Context, id ID, locale i18n.Locale, value string) error {
	if err := service.checkTranslation(ctx, id, locale, value); err != nil {
		return err
	}
	return service.store.SetManualTranslation(ctx, id, locale, value)
}

// SetMachineTranslation stores a machine translation made for
// sourceHash, and reports whether it was stored: it is ignored when a
// manual translation exists or when the source changed since the
// translation was requested (sourceHash is no longer current).
func (service *Service) SetMachineTranslation(ctx context.Context, id ID, locale i18n.Locale, value, sourceHash string) (bool, error) {
	if err := service.checkTranslation(ctx, id, locale, value); err != nil {
		return false, err
	}
	return service.store.SetMachineTranslation(ctx, id, locale, value, sourceHash)
}

// Delete deletes texts and their translations. Call it in the same
// transaction that deletes (or stops referencing) them, so no text is
// left orphaned.
func (service *Service) Delete(ctx context.Context, ids ...ID) error {
	if len(ids) == 0 {
		return nil
	}
	_, err := service.store.Delete(ctx, ids)
	return err
}

// DeleteOrphans deletes up to limit texts, last updated more than
// olderThan ago, that no declared Field references, and returns how many
// it deleted. It is the periodic garbage collection behind Delete; run
// it per tenant transaction. olderThan leaves room for transactions that
// created a text but have not committed the referencing row yet.
func (service *Service) DeleteOrphans(ctx context.Context, olderThan time.Duration, limit int) (int64, error) {
	fields := service.Fields()
	if len(fields) == 0 {
		return 0, ErrNoFields
	}
	if limit <= 0 {
		return 0, errors.New("localizedtext: DeleteOrphans limit must be positive")
	}
	return service.store.DeleteOrphans(ctx, fields, olderThan, limit)
}

func (service *Service) checkTranslation(ctx context.Context, id ID, locale i18n.Locale, value string) error {
	if value == "" {
		return ErrEmptyValue
	}
	if !service.supports(locale) {
		return fmt.Errorf("%w: %s", ErrUnsupportedLocale, locale)
	}
	text, err := service.store.LoadForLocale(ctx, []ID{id}, locale)
	if err != nil {
		return err
	}
	if len(text) == 0 {
		return ErrNotFound
	}
	if text[0].SourceLocale == locale {
		return ErrSourceLocale
	}
	return nil
}

func (service *Service) supports(locale i18n.Locale) bool {
	return !locale.IsZero() && slices.Contains(service.locales.Supported(), locale)
}

// request marks requests pending and hands them to the requester; it is
// a no-op without a requester.
func (service *Service) request(ctx context.Context, requests []TranslationRequest) error {
	if service.requester == nil || len(requests) == 0 {
		return nil
	}
	recorded, err := service.store.MarkPending(ctx, requests)
	if err != nil || len(recorded) == 0 {
		return err
	}
	return service.requester.RequestTranslations(ctx, recorded)
}

// FieldTexts is the API for the texts of one declared Field.
type FieldTexts struct {
	service *Service
	field   Field
}

// Field returns the declared field.
func (texts *FieldTexts) Field() Field {
	return texts.field
}

// Create stores a new text from source and requests its machine
// translations. Call it in the transaction that stores the referencing
// entity.
func (texts *FieldTexts) Create(ctx context.Context, source Source) (ID, error) {
	text, err := texts.newText(source)
	if err != nil {
		return ID{}, err
	}
	if err := texts.service.store.Create(ctx, text); err != nil {
		return ID{}, err
	}
	if err := texts.service.request(ctx, texts.plan(text, nil)); err != nil {
		return ID{}, err
	}
	return text.ID, nil
}

// UpdateSource replaces the source of text id. When the source changes,
// every translation of the old source becomes stale: machine ones are
// requested again, manual ones stay manual and are only flagged.
func (texts *FieldTexts) UpdateSource(ctx context.Context, id ID, source Source) error {
	text, err := texts.newText(source)
	if err != nil {
		return err
	}
	text.ID = id
	updated, err := texts.service.store.UpdateSource(ctx, text)
	if err != nil {
		return err
	}
	return texts.service.request(ctx, texts.plan(updated, updated.Translations))
}

// Localize returns text id in locale, falling back to the source.
func (texts *FieldTexts) Localize(ctx context.Context, id ID, locale i18n.Locale) (Localized, error) {
	localized, err := texts.LocalizeMany(ctx, []ID{id}, locale)
	if err != nil {
		return Localized{}, err
	}
	result, ok := localized[id]
	if !ok {
		return Localized{}, ErrNotFound
	}
	return result, nil
}

// LocalizeMany localizes many texts in one round trip, for lists. IDs
// that do not exist are omitted from the result.
func (texts *FieldTexts) LocalizeMany(ctx context.Context, ids []ID, locale i18n.Locale) (map[ID]Localized, error) {
	requested := texts.service.locales.Resolve(locale)
	loaded, err := texts.service.store.LoadForLocale(ctx, ids, requested)
	if err != nil {
		return nil, err
	}
	result := make(map[ID]Localized, len(loaded))
	var requests []TranslationRequest
	for _, text := range loaded {
		localized, request, needed := texts.localize(text, requested)
		result[text.ID] = localized
		if needed {
			requests = append(requests, request)
		}
	}
	if err := texts.service.request(ctx, requests); err != nil {
		return nil, err
	}
	return result, nil
}

// Get returns a text with all its translations.
func (texts *FieldTexts) Get(ctx context.Context, id ID) (Text, error) {
	return texts.service.Get(ctx, id)
}

// SetManualTranslation stores a person's translation; see
// Service.SetManualTranslation.
func (texts *FieldTexts) SetManualTranslation(ctx context.Context, id ID, locale i18n.Locale, value string) error {
	return texts.service.SetManualTranslation(ctx, id, locale, value)
}

// Delete deletes texts; see Service.Delete.
func (texts *FieldTexts) Delete(ctx context.Context, ids ...ID) error {
	return texts.service.Delete(ctx, ids...)
}

func (texts *FieldTexts) newText(source Source) (Text, error) {
	if source.Value == "" {
		return Text{}, ErrEmptyValue
	}
	locale := source.Locale
	if locale.IsZero() {
		locale = texts.service.locales.Source()
	}
	if !texts.service.supports(locale) {
		return Text{}, fmt.Errorf("%w: %s", ErrUnsupportedLocale, locale)
	}
	return Text{
		ID:           NewID(),
		SourceLocale: locale,
		SourceValue:  source.Value,
		SourceHash:   Hash(locale, source.Value),
		Context:      source.Context,
	}, nil
}

// plan returns the machine translations text needs, given its existing
// translations: every supported locale but the source, skipping manual
// and current or pending machine translations.
func (texts *FieldTexts) plan(text Text, existing []Translation) []TranslationRequest {
	var requests []TranslationRequest
	for _, locale := range texts.service.locales.Supported() {
		if locale == text.SourceLocale {
			continue
		}
		reason := ReasonMissing
		for _, translation := range existing {
			if translation.Locale != locale {
				continue
			}
			if translation.Origin == OriginManual || translation.Status != StatusStale {
				reason = ""
			} else {
				reason = ReasonStale
			}
		}
		if reason != "" {
			requests = append(requests, texts.requestFor(text, locale, reason))
		}
	}
	return requests
}

// localize resolves text for requested and says whether a machine
// translation must be requested.
func (texts *FieldTexts) localize(text Text, requested i18n.Locale) (Localized, TranslationRequest, bool) {
	source := Localized{
		ID: text.ID, Value: text.SourceValue, Locale: text.SourceLocale, Requested: requested,
		Origin: OriginSource, Status: StatusCurrent,
	}
	if requested == text.SourceLocale {
		return source, TranslationRequest{}, false
	}
	source.Fallback = true
	translation, ok := text.Translation(requested)
	if !ok {
		return source, texts.requestFor(text, requested, ReasonMissing), true
	}
	stale := translation.Origin == OriginMachine && translation.Status == StatusStale
	if translation.Value == "" {
		return source, texts.requestFor(text, requested, ReasonStale), stale
	}
	localized := Localized{
		ID: text.ID, Value: translation.Value, Locale: requested, Requested: requested,
		Origin: translation.Origin, Status: translation.Status,
	}
	return localized, texts.requestFor(text, requested, ReasonStale), stale
}

func (texts *FieldTexts) requestFor(text Text, locale i18n.Locale, reason Reason) TranslationRequest {
	context := text.Context
	if context == "" {
		context = texts.field.Context
	}
	return TranslationRequest{TextID: text.ID, Locale: locale, SourceHash: text.SourceHash, Context: context, Reason: reason}
}
