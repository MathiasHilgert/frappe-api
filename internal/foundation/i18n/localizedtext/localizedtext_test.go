package localizedtext_test

import (
	"context"
	"errors"
	"slices"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext"
)

var (
	spanish    = i18n.MustParseLocale("es-419")
	english    = i18n.MustParseLocale("en")
	portuguese = i18n.MustParseLocale("pt-BR")
)

var dishDescription = localizedtext.Field{
	Table:   "dishes",
	Column:  "description_text_id",
	Context: "Description of a dish on a restaurant menu",
}

func testCatalog(t *testing.T) *i18n.Catalog {
	t.Helper()
	catalog, err := i18n.NewCatalog(i18n.Settings{
		Source:    spanish,
		Supported: []i18n.Locale{spanish, english, portuguese},
		Messages:  i18n.EmbeddedMessages,
	})
	if err != nil {
		t.Fatalf("NewCatalog: %v", err)
	}
	return catalog
}

// fakeStore serves LoadForLocale and MarkPending from memory; every other
// Store method panics through the nil embedded interface.
type fakeStore struct {
	localizedtext.Store
	texts      map[localizedtext.ID]localizedtext.Text
	pending    []localizedtext.TranslationRequest
	references []localizedtext.Reference
	swept      []localizedtext.Reference
}

func (store *fakeStore) LoadForLocale(_ context.Context, ids []localizedtext.ID, locale i18n.Locale) ([]localizedtext.Text, error) {
	loaded := make([]localizedtext.Text, 0, len(ids))
	for _, id := range ids {
		text, ok := store.texts[id]
		if !ok {
			continue
		}
		text.Translations = slices.DeleteFunc(slices.Clone(text.Translations), func(translation localizedtext.Translation) bool {
			return translation.Locale != locale
		})
		loaded = append(loaded, text)
	}
	return loaded, nil
}

func (store *fakeStore) Isolate(ctx context.Context, work func(ctx context.Context) error) error {
	return work(ctx)
}

func (store *fakeStore) References(context.Context) ([]localizedtext.Reference, error) {
	return store.references, nil
}

func (store *fakeStore) DeleteOrphans(_ context.Context, references []localizedtext.Reference, _ time.Duration, _ int) (int64, error) {
	store.swept = references
	return 0, nil
}

func (store *fakeStore) MarkPending(_ context.Context, requests []localizedtext.TranslationRequest, _ time.Duration) ([]localizedtext.TranslationRequest, error) {
	store.pending = append(store.pending, requests...)
	return requests, nil
}

type recordingRequester struct {
	requests []localizedtext.TranslationRequest
}

func (requester *recordingRequester) RequestTranslations(_ context.Context, requests []localizedtext.TranslationRequest) error {
	requester.requests = append(requester.requests, requests...)
	return nil
}

type fixture struct {
	store     *fakeStore
	requester *recordingRequester
	texts     *localizedtext.FieldTexts
}

func newFixture(t *testing.T, texts ...localizedtext.Text) fixture {
	t.Helper()
	store := &fakeStore{texts: map[localizedtext.ID]localizedtext.Text{}}
	for _, text := range texts {
		store.texts[text.ID] = text
	}
	requester := &recordingRequester{}
	service, err := localizedtext.NewService(localizedtext.Settings{Store: store, Locales: testCatalog(t), Requester: requester})
	if err != nil {
		t.Fatalf("NewService: %v", err)
	}
	fieldTexts, err := service.Field(dishDescription)
	if err != nil {
		t.Fatalf("Field: %v", err)
	}
	return fixture{store: store, requester: requester, texts: fieldTexts}
}

func sourceText(translations ...localizedtext.Translation) localizedtext.Text {
	return localizedtext.Text{
		ID:           localizedtext.NewID(),
		SourceLocale: spanish,
		SourceValue:  "Milanesa napolitana",
		SourceHash:   localizedtext.Hash(spanish, "Milanesa napolitana"),
		Translations: translations,
	}
}

func TestHashDependsOnLocaleAndValue(t *testing.T) {
	t.Parallel()
	base := localizedtext.Hash(spanish, "Hola")
	if len(base) != 64 {
		t.Fatalf("Hash length = %d, want 64 hexadecimal characters", len(base))
	}
	if base != localizedtext.Hash(spanish, "Hola") {
		t.Fatal("Hash is not deterministic")
	}
	if base == localizedtext.Hash(spanish, "Hola!") {
		t.Fatal("Hash ignores the value")
	}
	if base == localizedtext.Hash(english, "Hola") {
		t.Fatal("Hash ignores the locale")
	}
}

func TestParseIDRoundTrips(t *testing.T) {
	t.Parallel()
	id := localizedtext.NewID()
	parsed, err := localizedtext.ParseID(id.String())
	if err != nil || parsed != id {
		t.Fatalf("ParseID(%q) = %v, %v; want %v", id, parsed, err, id)
	}
	if _, err := localizedtext.ParseID("not-a-uuid"); err == nil {
		t.Fatal("ParseID accepted an invalid ID")
	}
	if !(localizedtext.ID{}).IsZero() || id.IsZero() {
		t.Fatal("IsZero is wrong")
	}
}

func TestNewServiceRequiresStoreAndLocales(t *testing.T) {
	t.Parallel()
	if _, err := localizedtext.NewService(localizedtext.Settings{Locales: testCatalog(t)}); err == nil {
		t.Error("NewService accepted a nil Store")
	}
	if _, err := localizedtext.NewService(localizedtext.Settings{Store: &fakeStore{}}); err == nil {
		t.Error("NewService accepted nil Locales")
	}
}

func TestFieldRejectsInvalidDeclarations(t *testing.T) {
	t.Parallel()
	service, err := localizedtext.NewService(localizedtext.Settings{Store: &fakeStore{}, Locales: testCatalog(t)})
	if err != nil {
		t.Fatalf("NewService: %v", err)
	}
	invalid := []localizedtext.Field{
		{Table: "", Column: "description_text_id"},
		{Table: "dishes", Column: ""},
		{Table: "dishes; DROP TABLE x", Column: "description_text_id"},
		{Table: "dishes", Column: "Description"},
	}
	for _, field := range invalid {
		if _, err := service.Field(field); !errors.Is(err, localizedtext.ErrInvalidField) {
			t.Errorf("Field(%+v) error = %v, want ErrInvalidField", field, err)
		}
	}
	if _, err := service.Field(dishDescription); err != nil {
		t.Fatalf("Field(valid) = %v", err)
	}
	if _, err := service.Field(dishDescription); err != nil {
		t.Fatalf("declaring the same field twice must be idempotent, got %v", err)
	}
	if got := service.Fields(); len(got) != 1 || got[0] != dishDescription {
		t.Fatalf("Fields() = %+v, want only %+v", got, dishDescription)
	}
	conflicting := dishDescription
	conflicting.Context = "something else"
	if _, err := service.Field(conflicting); !errors.Is(err, localizedtext.ErrInvalidField) {
		t.Fatalf("redeclaring a field with another context error = %v, want ErrInvalidField", err)
	}
}

func TestLocalizeServesTheSourceInTheSourceLocale(t *testing.T) {
	t.Parallel()
	text := sourceText()
	subject := newFixture(t, text)
	localized, err := subject.texts.Localize(context.Background(), text.ID, i18n.MustParseLocale("es-MX"))
	if err != nil {
		t.Fatalf("Localize: %v", err)
	}
	if localized.Value != text.SourceValue || localized.Locale != spanish || localized.Origin != localizedtext.OriginSource || localized.Fallback {
		t.Fatalf("Localize = %+v, want the source, not a fallback", localized)
	}
	if len(subject.requester.requests) != 0 {
		t.Fatalf("requests = %+v, want none", subject.requester.requests)
	}
}

func TestLocalizeFallsBackToTheSourceAndRequestsAMissingTranslation(t *testing.T) {
	t.Parallel()
	text := sourceText()
	subject := newFixture(t, text)
	localized, err := subject.texts.Localize(context.Background(), text.ID, english)
	if err != nil {
		t.Fatalf("Localize: %v", err)
	}
	if localized.Value != text.SourceValue || localized.Locale != spanish || !localized.Fallback || localized.Requested != english {
		t.Fatalf("Localize = %+v, want a fallback to the source", localized)
	}
	want := []localizedtext.TranslationRequest{{
		TextID: text.ID, Locale: english, SourceHash: text.SourceHash,
		Context: dishDescription.Context, Reason: localizedtext.ReasonMissing,
	}}
	if !slices.Equal(subject.requester.requests, want) || !slices.Equal(subject.store.pending, want) {
		t.Fatalf("requests = %+v, pending = %+v; want %+v", subject.requester.requests, subject.store.pending, want)
	}
}

func TestLocalizePrefersTheTextContextOverTheFieldDefault(t *testing.T) {
	t.Parallel()
	text := sourceText()
	text.Context = "Kids menu"
	subject := newFixture(t, text)
	if _, err := subject.texts.Localize(context.Background(), text.ID, english); err != nil {
		t.Fatalf("Localize: %v", err)
	}
	if len(subject.requester.requests) != 1 || subject.requester.requests[0].Context != "Kids menu" {
		t.Fatalf("requests = %+v, want context %q", subject.requester.requests, "Kids menu")
	}
}

func TestLocalizeServesStaleTranslationsButRegeneratesOnlyMachineOnes(t *testing.T) {
	t.Parallel()
	machine := sourceText(localizedtext.Translation{Locale: english, Value: "Old", Origin: localizedtext.OriginMachine, Status: localizedtext.StatusStale})
	manual := sourceText(localizedtext.Translation{Locale: english, Value: "Mine", Origin: localizedtext.OriginManual, Status: localizedtext.StatusStale})
	subject := newFixture(t, machine, manual)

	result, err := subject.texts.LocalizeMany(context.Background(), []localizedtext.ID{machine.ID, manual.ID}, english)
	if err != nil {
		t.Fatalf("LocalizeMany: %v", err)
	}
	if got := result[machine.ID]; got.Value != "Old" || got.Status != localizedtext.StatusStale || got.Fallback {
		t.Errorf("machine = %+v, want the stale machine value", got)
	}
	if got := result[manual.ID]; got.Value != "Mine" || got.Origin != localizedtext.OriginManual || got.Status != localizedtext.StatusStale {
		t.Errorf("manual = %+v, want the stale manual value", got)
	}
	if len(subject.requester.requests) != 1 || subject.requester.requests[0].TextID != machine.ID || subject.requester.requests[0].Reason != localizedtext.ReasonStale {
		t.Fatalf("requests = %+v, want one stale request for the machine translation only", subject.requester.requests)
	}
}

func TestLocalizeDoesNotRerequestPendingTranslations(t *testing.T) {
	t.Parallel()
	text := sourceText(localizedtext.Translation{Locale: english, Origin: localizedtext.OriginMachine, Status: localizedtext.StatusPending, RequestedAt: time.Now()})
	subject := newFixture(t, text)
	localized, err := subject.texts.Localize(context.Background(), text.ID, english)
	if err != nil {
		t.Fatalf("Localize: %v", err)
	}
	if !localized.Fallback || localized.Value != text.SourceValue {
		t.Fatalf("Localize = %+v, want the source while pending", localized)
	}
	if len(subject.requester.requests) != 0 {
		t.Fatalf("requests = %+v, want none while pending", subject.requester.requests)
	}
}

func TestLocalizeReportsUnknownTexts(t *testing.T) {
	t.Parallel()
	subject := newFixture(t)
	if _, err := subject.texts.Localize(context.Background(), localizedtext.NewID(), english); !errors.Is(err, localizedtext.ErrNotFound) {
		t.Fatalf("Localize(unknown) error = %v, want ErrNotFound", err)
	}
	result, err := subject.texts.LocalizeMany(context.Background(), []localizedtext.ID{localizedtext.NewID()}, english)
	if err != nil || len(result) != 0 {
		t.Fatalf("LocalizeMany(unknown) = %v, %v; want an empty map (missing IDs are omitted)", result, err)
	}
}

func TestWithoutRequesterNothingIsMarkedPending(t *testing.T) {
	t.Parallel()
	text := sourceText()
	store := &fakeStore{texts: map[localizedtext.ID]localizedtext.Text{text.ID: text}}
	service, err := localizedtext.NewService(localizedtext.Settings{Store: store, Locales: testCatalog(t)})
	if err != nil {
		t.Fatalf("NewService: %v", err)
	}
	fieldTexts, err := service.Field(dishDescription)
	if err != nil {
		t.Fatalf("Field: %v", err)
	}
	if _, err := fieldTexts.Localize(context.Background(), text.ID, english); err != nil {
		t.Fatalf("Localize: %v", err)
	}
	if len(store.pending) != 0 {
		t.Fatalf("pending = %+v, want none without a requester", store.pending)
	}
}

func TestDeleteOrphansRequiresDeclaredFieldsAndALimit(t *testing.T) {
	t.Parallel()
	service, err := localizedtext.NewService(localizedtext.Settings{Store: &fakeStore{}, Locales: testCatalog(t)})
	if err != nil {
		t.Fatalf("NewService: %v", err)
	}
	if _, err := service.DeleteOrphans(context.Background(), 0, 10); !errors.Is(err, localizedtext.ErrNoFields) {
		t.Fatalf("DeleteOrphans without fields error = %v, want ErrNoFields", err)
	}
	if _, err := service.Field(dishDescription); err != nil {
		t.Fatalf("Field: %v", err)
	}
	if _, err := service.DeleteOrphans(context.Background(), 0, 0); err == nil {
		t.Fatal("DeleteOrphans accepted a zero limit")
	}
}
