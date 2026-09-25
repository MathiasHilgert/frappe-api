package localizedtext

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"slices"
	"strings"
	"sync"
	"time"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/metric"
	"go.opentelemetry.io/otel/trace"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
)

// DefaultPendingTimeout is how long a requested machine translation may
// stay pending before it is requested again.
const DefaultPendingTimeout = 15 * time.Minute

// DefaultMaxRequestAttempts is how many times a machine translation is
// requested before the expired sweep gives up on it (StatusFailed).
const DefaultMaxRequestAttempts = 5

// instrumentationName names the meter the Service reports on.
const instrumentationName = "github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext"

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
	// Now is the clock; nil means time.Now.
	Now func() time.Time
	// Logger reports failed best-effort requests; nil means
	// slog.Default().
	Logger *slog.Logger
	// PendingTimeout is how long a translation may stay pending before it
	// is requested again (the request was lost). Zero means
	// DefaultPendingTimeout.
	PendingTimeout time.Duration
	// MaxRequestAttempts is how many requests a pending translation gets
	// before RequestExpired marks it failed. Zero means
	// DefaultMaxRequestAttempts.
	MaxRequestAttempts int
}

// Service is the localized text API. Build one per application and hand
// each module the FieldTexts of the fields it declares.
type Service struct {
	store          Store
	locales        LocaleResolver
	requester      TranslationRequester
	failures       metric.Int64Counter
	now            func() time.Time
	logger         *slog.Logger
	fields         map[[2]string]Field
	pendingTimeout time.Duration
	maxAttempts    int
	mutex          sync.Mutex
}

// NewService validates settings and returns a Service.
func NewService(settings Settings) (*Service, error) {
	if settings.Store == nil {
		return nil, errors.New("localizedtext: Settings.Store is required")
	}
	if settings.Locales == nil {
		return nil, errors.New("localizedtext: Settings.Locales is required")
	}
	failures, err := otel.Meter(instrumentationName).Int64Counter("frappe.localizedtext.request.failures",
		metric.WithDescription("Translation requests that failed during a read and were skipped."))
	if err != nil {
		return nil, fmt.Errorf("localizedtext: create metric: %w", err)
	}
	service := &Service{
		store:          settings.Store,
		locales:        settings.Locales,
		requester:      settings.Requester,
		failures:       failures,
		now:            settings.Now,
		logger:         settings.Logger,
		pendingTimeout: settings.PendingTimeout,
		maxAttempts:    settings.MaxRequestAttempts,
		fields:         map[[2]string]Field{},
	}
	if service.now == nil {
		service.now = time.Now
	}
	if service.logger == nil {
		service.logger = slog.Default()
	}
	if service.pendingTimeout <= 0 {
		service.pendingTimeout = DefaultPendingTimeout
	}
	if service.maxAttempts <= 0 {
		service.maxAttempts = DefaultMaxRequestAttempts
	}
	return service, nil
}

// Field declares field and returns the API for its texts. Declaring the
// same field again is idempotent; declaring it with another Context
// fails. Declare every referencing column at startup: DeleteOrphans
// refuses to run while a foreign key to localized_texts is undeclared.
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
// source changes it is only marked stale. A global text returns
// ErrReadOnlyText.
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

// ExpiredPending returns up to limit of the current tenant's pending
// machine translations requested more than PendingTimeout ago, for the
// machine translation sweeper to request again. Context is the text's own
// (empty means the referencing Field's default).
func (service *Service) ExpiredPending(ctx context.Context, limit int) ([]TranslationRequest, error) {
	return service.store.ExpiredPending(ctx, service.pendingTimeout, limit)
}

// RequestExpired requests again up to limit of the current tenant's
// pending machine translations whose request expired (older than
// PendingTimeout; the job was lost or gave up), and returns how many it
// requested. One requested MaxRequestAttempts times already is marked
// failed instead. A text without its own Context gets the default Context
// of the declared Field that references it. Without a requester it does
// nothing.
func (service *Service) RequestExpired(ctx context.Context, limit int) (int, error) {
	if service.requester == nil {
		return 0, nil
	}
	expired, err := service.ExpiredPending(ctx, limit)
	if err != nil || len(expired) == 0 {
		return 0, err
	}
	requests, err := service.giveUpExhausted(ctx, expired)
	if err != nil || len(requests) == 0 {
		return 0, err
	}
	if err = service.defaultContexts(ctx, requests); err != nil {
		return 0, err
	}
	recorded, err := service.store.MarkPending(ctx, requests, service.pendingTimeout)
	if err != nil || len(recorded) == 0 {
		return 0, err
	}
	if err := service.requester.RequestTranslations(ctx, recorded); err != nil {
		return 0, err
	}
	return len(recorded), nil
}

// giveUpExhausted marks failed the requests that reached
// MaxRequestAttempts and returns the others.
func (service *Service) giveUpExhausted(ctx context.Context, expired []TranslationRequest) ([]TranslationRequest, error) {
	exhausted := map[i18n.Locale][]ID{}
	requests := make([]TranslationRequest, 0, len(expired))
	for _, request := range expired {
		if request.Attempts >= service.maxAttempts {
			exhausted[request.Locale] = append(exhausted[request.Locale], request.TextID)
			continue
		}
		requests = append(requests, request)
	}
	for locale, ids := range exhausted {
		if err := service.store.MarkFailed(ctx, locale, ids); err != nil {
			return nil, err
		}
	}
	return requests, nil
}

// LeasePending keeps the pending machine translations of ids into locale
// from being requested again until until, while a job works on them.
func (service *Service) LeasePending(ctx context.Context, locale i18n.Locale, ids []ID, until time.Time) error {
	if len(ids) == 0 {
		return nil
	}
	return service.store.Lease(ctx, locale, ids, until)
}

// MarkFailed gives up the pending machine translations of ids into
// locale (StatusFailed) until their source changes.
func (service *Service) MarkFailed(ctx context.Context, locale i18n.Locale, ids []ID) error {
	if len(ids) == 0 {
		return nil
	}
	return service.store.MarkFailed(ctx, locale, ids)
}

// defaultContexts fills the Context of requests that have none with the
// default Context of the declared Field referencing their text.
func (service *Service) defaultContexts(ctx context.Context, requests []TranslationRequest) error {
	var ids []ID
	for _, request := range requests {
		if request.Context == "" {
			ids = append(ids, request.TextID)
		}
	}
	fields := service.Fields()
	if len(ids) == 0 || len(fields) == 0 {
		return nil
	}
	references := make([]Reference, len(fields))
	contexts := make(map[Reference]string, len(fields))
	for index, field := range fields {
		references[index] = Reference{Table: field.Table, Column: field.Column}
		contexts[references[index]] = field.Context
	}
	referencing, err := service.store.Referencing(ctx, references, ids)
	if err != nil {
		return err
	}
	for index := range requests {
		if reference, ok := referencing[requests[index].TextID]; ok && requests[index].Context == "" {
			requests[index].Context = contexts[Reference{Table: reference.Table, Column: reference.Column}]
		}
	}
	return nil
}

// Tenants lists every tenant owning at least one text, so a periodic
// sweep can run RequestExpired and DeleteOrphans once per tenant
// transaction.
func (service *Service) Tenants(ctx context.Context) ([]string, error) {
	return service.store.Tenants(ctx)
}

// DeleteOrphans deletes up to limit texts, last updated more than
// olderThan ago, that nothing references, and returns how many it
// deleted. It is the periodic garbage collection behind Delete; run it
// per tenant transaction. olderThan leaves room for transactions that
// created a text but have not committed the referencing row yet.
//
// The foreign keys to localized_texts in the database catalog are the
// source of truth for what references a text, so no column can be
// missed; they must match the declared Fields exactly (declarations also
// carry the machine translation context) and use ON DELETE NO ACTION or
// RESTRICT. On any mismatch it deletes nothing and returns
// ErrUndeclaredReference, ErrMissingForeignKey or ErrUnsafeForeignKey.
func (service *Service) DeleteOrphans(ctx context.Context, olderThan time.Duration, limit int) (int64, error) {
	fields := service.Fields()
	if len(fields) == 0 {
		return 0, ErrNoFields
	}
	if limit <= 0 {
		return 0, errors.New("localizedtext: DeleteOrphans limit must be positive")
	}
	references, err := service.store.References(ctx)
	if err != nil {
		return 0, err
	}
	if err := checkReferences(fields, references); err != nil {
		return 0, err
	}
	return service.store.DeleteOrphans(ctx, references, olderThan, limit)
}

func checkReferences(fields []Field, references []Reference) error {
	declared := make(map[[2]string]bool, len(fields))
	for _, field := range fields {
		declared[[2]string{field.Table, field.Column}] = true
	}
	found := make(map[[2]string]bool, len(references))
	for _, reference := range references {
		key := [2]string{reference.Table, reference.Column}
		if reference.OnDelete != OnDeleteNoAction && reference.OnDelete != OnDeleteRestrict {
			return fmt.Errorf("%w: %s.%s is ON DELETE %s", ErrUnsafeForeignKey, reference.Table, reference.Column, reference.OnDelete)
		}
		if !declared[key] {
			return fmt.Errorf("%w: %s.%s", ErrUndeclaredReference, reference.Table, reference.Column)
		}
		found[key] = true
	}
	for _, field := range fields {
		if !found[[2]string{field.Table, field.Column}] {
			return fmt.Errorf("%w: %s.%s", ErrMissingForeignKey, field.Table, field.Column)
		}
	}
	return nil
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
// a no-op without a requester. Writes propagate its failures, so they
// roll back with the change that needed the translations.
func (service *Service) request(ctx context.Context, requests []TranslationRequest) error {
	if service.requester == nil || len(requests) == 0 {
		return nil
	}
	recorded, err := service.store.MarkPending(ctx, requests, service.pendingTimeout)
	if err != nil || len(recorded) == 0 {
		return err
	}
	return service.requester.RequestTranslations(ctx, recorded)
}

// requestBestEffort is request for reads: it runs isolated (a savepoint),
// so a failure, including a read-only transaction rejecting the pending
// rows, breaks neither the read nor its transaction. Failures are logged,
// counted and added to the current span; the pending timeout retries.
func (service *Service) requestBestEffort(ctx context.Context, requests []TranslationRequest) {
	if service.requester == nil || len(requests) == 0 {
		return
	}
	err := service.store.Isolate(ctx, func(ctx context.Context) error {
		return service.request(ctx, requests)
	})
	if err == nil {
		return
	}
	service.failures.Add(ctx, 1)
	trace.SpanFromContext(ctx).AddEvent("localizedtext.request_failed", trace.WithAttributes(
		attribute.String("error", err.Error()), attribute.Int("requests", len(requests))))
	service.logger.WarnContext(ctx, "localized text translation request failed, serving the read anyway",
		slog.Int("requests", len(requests)), slog.Any("error", err))
}

// reasonFor says why an existing translation must be requested again, or
// "" when it must not: manual and current ones never, stale machine ones
// always, pending ones once their request expired.
func (service *Service) reasonFor(translation Translation) Reason {
	switch {
	case translation.Origin != OriginMachine:
		return ""
	case translation.Status == StatusStale:
		return ReasonStale
	case translation.Status == StatusPending && service.now().Sub(translation.RequestedAt) > service.pendingTimeout:
		return ReasonExpired
	default:
		return ""
	}
}

// FieldTexts is the API for the texts of one declared Field. Create,
// UpdateSource and the Localize methods use the Field's default Context.
// Get, SetManualTranslation and Delete are field-agnostic: a text does
// not record which column references it (it is created before the
// entity row), so they act on any text of the tenant.
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
// requested again, manual ones stay manual and are only flagged. An
// empty source.Context keeps the stored context; set ClearContext to
// remove it. A global text returns ErrReadOnlyText.
func (texts *FieldTexts) UpdateSource(ctx context.Context, id ID, source Source) error {
	text, err := texts.newText(source)
	if err != nil {
		return err
	}
	source.Locale = text.SourceLocale
	updated, err := texts.service.store.UpdateSource(ctx, id, source, text.SourceHash)
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
// that do not exist are omitted from the result. Missing, stale or
// expired pending machine translations are requested best effort: that
// writes pending rows, so it needs a writable transaction; in a read-only
// one the request fails, is logged, and the read still succeeds.
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
	texts.service.requestBestEffort(ctx, requests)
	return result, nil
}

// Get returns a text with all its translations (field-agnostic).
func (texts *FieldTexts) Get(ctx context.Context, id ID) (Text, error) {
	return texts.service.Get(ctx, id)
}

// SetManualTranslation stores a person's translation (field-agnostic);
// see Service.SetManualTranslation.
func (texts *FieldTexts) SetManualTranslation(ctx context.Context, id ID, locale i18n.Locale, value string) error {
	return texts.service.SetManualTranslation(ctx, id, locale, value)
}

// Delete deletes texts (field-agnostic); see Service.Delete.
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
// translations: every supported locale but the source that has no
// translation yet or one reasonFor wants requested again.
func (texts *FieldTexts) plan(text Text, existing []Translation) []TranslationRequest {
	var requests []TranslationRequest
	for _, locale := range texts.service.locales.Supported() {
		if locale == text.SourceLocale {
			continue
		}
		reason := ReasonMissing
		for _, translation := range existing {
			if translation.Locale == locale {
				reason = texts.service.reasonFor(translation)
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
	reason := texts.service.reasonFor(translation)
	request := texts.requestFor(text, requested, reason)
	if translation.Value == "" {
		return source, request, reason != ""
	}
	localized := Localized{
		ID: text.ID, Value: translation.Value, Locale: requested, Requested: requested,
		Origin: translation.Origin, Status: translation.Status,
	}
	return localized, request, reason != ""
}

func (texts *FieldTexts) requestFor(text Text, locale i18n.Locale, reason Reason) TranslationRequest {
	context := text.Context
	if context == "" {
		context = texts.field.Context
	}
	return TranslationRequest{TextID: text.ID, Locale: locale, SourceHash: text.SourceHash, Context: context, Reason: reason}
}
