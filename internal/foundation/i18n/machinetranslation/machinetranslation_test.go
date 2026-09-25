package machinetranslation_test

import (
	"context"
	"errors"
	"sort"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/machinetranslation"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs/jobstest"
)

var (
	spanish = i18n.MustParseLocale("es-419")
	english = i18n.MustParseLocale("en")
	french  = i18n.MustParseLocale("fr")
)

// fakeTexts is an in-memory machinetranslation.Texts.
type fakeTexts struct {
	texts         map[localizedtext.ID]localizedtext.Text
	stored        map[localizedtext.ID]string
	orphanErrors  map[string]error
	leased        map[localizedtext.ID]time.Time
	failed        map[localizedtext.ID]bool
	tenantsSeen   []string
	tenants       []string
	expiredCalled int
	orphansCalled int
	mutex         sync.Mutex
}

func newFakeTexts(texts ...localizedtext.Text) *fakeTexts {
	fake := &fakeTexts{
		texts: map[localizedtext.ID]localizedtext.Text{}, stored: map[localizedtext.ID]string{},
		leased: map[localizedtext.ID]time.Time{}, failed: map[localizedtext.ID]bool{},
	}
	for _, text := range texts {
		fake.texts[text.ID] = text
	}
	return fake
}

func (fake *fakeTexts) Get(_ context.Context, id localizedtext.ID) (localizedtext.Text, error) {
	text, ok := fake.texts[id]
	if !ok {
		return localizedtext.Text{}, localizedtext.ErrNotFound
	}
	return text, nil
}

func (fake *fakeTexts) SetMachineTranslation(_ context.Context, id localizedtext.ID, _ i18n.Locale, value, sourceHash string) (bool, error) {
	if value == "" {
		return false, localizedtext.ErrEmptyValue
	}
	if fake.texts[id].SourceHash != sourceHash {
		return false, nil
	}
	fake.stored[id] = value
	return true, nil
}

func (fake *fakeTexts) RequestExpired(context.Context, int) (int, error) {
	fake.expiredCalled++
	return 1, nil
}

func (fake *fakeTexts) DeleteOrphans(ctx context.Context, _ time.Duration, _ int) (int64, error) {
	fake.orphansCalled++
	return 0, fake.orphanErrors[tenantOf(ctx)]
}

func (fake *fakeTexts) LeasePending(_ context.Context, _ i18n.Locale, ids []localizedtext.ID, until time.Time) error {
	for _, id := range ids {
		fake.leased[id] = until
	}
	return nil
}

func (fake *fakeTexts) MarkFailed(_ context.Context, _ i18n.Locale, ids []localizedtext.ID) error {
	for _, id := range ids {
		fake.failed[id] = true
	}
	return nil
}

func (fake *fakeTexts) Tenants(context.Context) ([]string, error) {
	return fake.tenants, nil
}

type tenantKey struct{}

func tenantOf(ctx context.Context) string {
	tenant, _ := ctx.Value(tenantKey{}).(string)
	return tenant
}

// transactor records the tenant of every transaction.
func (fake *fakeTexts) transactor(ctx context.Context, tenant string, work func(ctx context.Context) error) error {
	fake.mutex.Lock()
	fake.tenantsSeen = append(fake.tenantsSeen, tenant)
	fake.mutex.Unlock()
	return work(context.WithValue(ctx, tenantKey{}, tenant))
}

// fakeTranslator prefixes every text with its target, or fails.
type fakeTranslator struct {
	failure  error
	fail     func(request machinetranslation.Request) error
	requests []machinetranslation.Request
}

func (translator *fakeTranslator) Translate(_ context.Context, request machinetranslation.Request) ([]string, error) {
	translator.requests = append(translator.requests, request)
	if translator.failure != nil {
		return nil, translator.failure
	}
	if translator.fail != nil {
		if err := translator.fail(request); err != nil {
			return nil, err
		}
	}
	translated := make([]string, len(request.Texts))
	for index, text := range request.Texts {
		translated[index] = request.Target.String() + ":" + text
	}
	return translated, nil
}

type harness struct {
	machine    *machinetranslation.MachineTranslation
	texts      *fakeTexts
	translator *fakeTranslator
	recorder   *jobstest.Recorder
}

func newHarness(t *testing.T, texts *fakeTexts, mutate ...func(*machinetranslation.Settings)) harness {
	t.Helper()
	catalog, recorder := jobstest.NewCatalog()
	translator := &fakeTranslator{}
	settings := machinetranslation.Settings{Translator: translator, Transactor: texts.transactor, BatchSize: 2}
	for _, change := range mutate {
		change(&settings)
	}
	module := catalog.Module("localized_texts")
	machine, err := machinetranslation.New(module, settings)
	if err != nil {
		t.Fatalf("New: %v", err)
	}
	machine.Register(module, texts)
	return harness{machine: machine, texts: texts, translator: translator, recorder: recorder}
}

func text(value string, translations ...localizedtext.Translation) localizedtext.Text {
	return localizedtext.Text{
		ID: localizedtext.NewID(), SourceLocale: spanish, SourceValue: value,
		SourceHash: localizedtext.Hash(spanish, value), Translations: translations,
	}
}

func arguments(locale i18n.Locale, hint string, texts ...localizedtext.Text) machinetranslation.TranslateArguments {
	args := machinetranslation.TranslateArguments{Locale: locale.String(), Context: hint}
	for _, text := range texts {
		args.Texts = append(args.Texts, machinetranslation.TextReference{ID: text.ID.String(), SourceHash: text.SourceHash})
	}
	return args
}

func TestRequesterEnqueuesBatchesPerLocaleAndContext(t *testing.T) {
	t.Parallel()
	tested := newHarness(t, newFakeTexts())
	first, second, third := localizedtext.NewID(), localizedtext.NewID(), localizedtext.NewID()
	requests := []localizedtext.TranslationRequest{
		{TextID: first, Locale: english, SourceHash: "h1", Context: "Dish"},
		{TextID: second, Locale: english, SourceHash: "h2", Context: "Dish"},
		{TextID: third, Locale: english, SourceHash: "h3", Context: "Dish"},
		{TextID: first, Locale: french, SourceHash: "h1", Context: "Dish"},
		{TextID: second, Locale: english, SourceHash: "h2", Context: "Section"},
	}
	if err := tested.machine.Requester().RequestTranslations(context.Background(), requests); err != nil {
		t.Fatalf("RequestTranslations: %v", err)
	}
	enqueued := jobstest.Enqueued(t, tested.recorder, tested.machine.TranslateJob())
	batches := make([]string, 0, len(enqueued))
	for _, job := range enqueued {
		// Deduplication is the pending row (see requester): a River
		// unique job would drop a request racing a running job.
		if job.Request.Unique != nil {
			t.Errorf("job %+v is unique, want plain", job.Args)
		}
		batches = append(batches, job.Args.Locale+"|"+job.Args.Context+"|"+string(rune('0'+len(job.Args.Texts))))
	}
	sort.Strings(batches)
	want := []string{"en|Dish|1", "en|Dish|2", "en|Section|1", "fr|Dish|1"}
	if strings.Join(batches, ",") != strings.Join(want, ",") {
		t.Fatalf("batches = %v, want %v (batch size 2)", batches, want)
	}
}

func TestTranslateStoresMachineTranslationsUnderTheJobTenant(t *testing.T) {
	t.Parallel()
	own := text("Milanesa")
	own.Context = "Own hint"
	plain := text("Empanada")
	texts := newFakeTexts(own, plain)
	tested := newHarness(t, texts)

	err := jobstest.Run(t, tested.machine.TranslateJob(), arguments(english, "Dish", own, plain), jobstest.Tenant("acme"))
	if err != nil {
		t.Fatalf("Run: %v", err)
	}
	if texts.stored[own.ID] != "en:Milanesa" || texts.stored[plain.ID] != "en:Empanada" {
		t.Fatalf("stored = %v", texts.stored)
	}
	for _, tenant := range texts.tenantsSeen {
		if tenant != "acme" {
			t.Fatalf("transaction tenants = %v, want only acme", texts.tenantsSeen)
		}
	}
	contexts := map[string]bool{}
	for _, request := range tested.translator.requests {
		contexts[request.Context] = true
		if request.Source != spanish || request.Target != english {
			t.Errorf("request languages = %s -> %s", request.Source, request.Target)
		}
	}
	if !contexts["Own hint"] || !contexts["Dish"] || len(contexts) != 2 {
		t.Fatalf("contexts = %v, want the text's own and the field default", contexts)
	}
}

func TestTranslateSkipsWhatNoLongerNeedsIt(t *testing.T) {
	t.Parallel()
	manual := text("Manual", localizedtext.Translation{Locale: english, Origin: localizedtext.OriginManual, Status: localizedtext.StatusStale})
	current := text("Current")
	current.Translations = []localizedtext.Translation{{Locale: english, Origin: localizedtext.OriginMachine, Status: localizedtext.StatusCurrent, SourceHash: current.SourceHash, Value: "x"}}
	changed := text("Changed")
	outdated := arguments(english, "", changed)
	outdated.Texts[0].SourceHash = "old hash"
	deleted := text("Deleted")
	texts := newFakeTexts(manual, current, changed)
	tested := newHarness(t, texts)

	args := arguments(english, "", manual, current, deleted)
	args.Texts = append(args.Texts, outdated.Texts...)
	if err := jobstest.Run(t, tested.machine.TranslateJob(), args, jobstest.Tenant("acme")); err != nil {
		t.Fatalf("Run: %v", err)
	}
	if len(tested.translator.requests) != 0 || len(texts.stored) != 0 {
		t.Fatalf("translated %v and stored %v, want nothing", tested.translator.requests, texts.stored)
	}
}

func TestTranslateDiscardsAResultWhoseSourceChangedMidFlight(t *testing.T) {
	t.Parallel()
	milanesa := text("Milanesa")
	texts := newFakeTexts(milanesa)
	tested := newHarness(t, texts)
	tested.translator.failure = nil
	// The source changes while DeepL translates: the store refuses the
	// old hash, and the job still succeeds.
	translator := &changingTranslator{fakeTranslator: tested.translator, texts: texts, id: milanesa.ID}
	tested = newHarness(t, texts, func(settings *machinetranslation.Settings) { settings.Translator = translator })
	if err := jobstest.Run(t, tested.machine.TranslateJob(), arguments(english, "", milanesa), jobstest.Tenant("acme")); err != nil {
		t.Fatalf("Run: %v", err)
	}
	if _, stored := texts.stored[milanesa.ID]; stored {
		t.Fatal("a translation of the old source was stored")
	}
}

type changingTranslator struct {
	*fakeTranslator
	texts *fakeTexts
	id    localizedtext.ID
}

func (translator *changingTranslator) Translate(ctx context.Context, request machinetranslation.Request) ([]string, error) {
	changed := translator.texts.texts[translator.id]
	changed.SourceValue = "Milanesa a caballo"
	changed.SourceHash = localizedtext.Hash(spanish, changed.SourceValue)
	translator.texts.texts[translator.id] = changed
	return translator.fakeTranslator.Translate(ctx, request)
}

func TestTranslateMapsProviderFailures(t *testing.T) {
	t.Parallel()
	cases := map[string]struct {
		failure error
		check   func(error) bool
	}{
		"rate limited snoozes for Retry-After": {
			machinetranslation.RateLimitedError{RetryAfter: 7 * time.Second},
			func(err error) bool { duration, ok := jobs.SnoozeDuration(err); return ok && duration == 7*time.Second },
		},
		"quota exceeded snoozes while the circuit is open": {
			machinetranslation.ErrQuotaExceeded,
			func(err error) bool {
				duration, ok := jobs.SnoozeDuration(err)
				return ok && duration == machinetranslation.DefaultQuotaPause
			},
		},
		"permanent cancels": {
			machinetranslation.PermanentError{Cause: errors.New("bad key")}, jobs.IsCancel,
		},
		"transient retries": {
			errors.New("connection reset"),
			func(err error) bool {
				_, snoozed := jobs.SnoozeDuration(err)
				return err != nil && !jobs.IsCancel(err) && !snoozed
			},
		},
	}
	for name, testCase := range cases {
		t.Run(name, func(t *testing.T) {
			t.Parallel()
			milanesa := text("Milanesa")
			tested := newHarness(t, newFakeTexts(milanesa))
			tested.translator.failure = testCase.failure
			err := jobstest.Run(t, tested.machine.TranslateJob(), arguments(english, "", milanesa), jobstest.Tenant("acme"))
			if !testCase.check(err) {
				t.Fatalf("Run = %v", err)
			}
		})
	}
}

func TestTranslateCancelsWithoutATenantOrATranslator(t *testing.T) {
	t.Parallel()
	milanesa := text("Milanesa")
	tested := newHarness(t, newFakeTexts(milanesa))
	if err := jobstest.Run(t, tested.machine.TranslateJob(), arguments(english, "", milanesa)); !jobs.IsCancel(err) {
		t.Fatalf("Run without tenant = %v, want a cancel", err)
	}
	disabled := newHarness(t, newFakeTexts(milanesa), func(settings *machinetranslation.Settings) { settings.Translator = nil })
	if disabled.machine.Requester() != nil {
		t.Fatal("Requester() is not nil while disabled")
	}
	if err := jobstest.Run(t, disabled.machine.TranslateJob(), arguments(english, "", milanesa), jobstest.Tenant("acme")); !jobs.IsCancel(err) {
		t.Fatalf("Run while disabled = %v, want a cancel", err)
	}
}

func TestSweepsRunOncePerTenant(t *testing.T) {
	t.Parallel()
	texts := newFakeTexts()
	texts.tenants = []string{"acme", "globex"}
	tested := newHarness(t, texts)
	if err := jobstest.Run(t, tested.machine.RequestExpiredJob(), machinetranslation.SweepArguments{}); err != nil {
		t.Fatalf("expired sweep: %v", err)
	}
	if err := jobstest.Run(t, tested.machine.DeleteOrphansJob(), machinetranslation.SweepArguments{}); err != nil {
		t.Fatalf("orphan sweep: %v", err)
	}
	if texts.expiredCalled != 2 || texts.orphansCalled != 2 {
		t.Fatalf("expired calls = %d, orphan calls = %d; want 2 each", texts.expiredCalled, texts.orphansCalled)
	}
	want := "|acme|globex||acme|globex"
	if got := strings.Join(texts.tenantsSeen, "|"); got != want {
		t.Fatalf("transaction tenants = %q, want %q (the listing runs without a tenant)", got, want)
	}
}

func TestOrphanSweepToleratesNoFieldsAndCancelsOnSchemaMismatch(t *testing.T) {
	t.Parallel()
	texts := newFakeTexts()
	texts.tenants = []string{"acme"}
	texts.orphanErrors = map[string]error{"acme": localizedtext.ErrNoFields}
	tested := newHarness(t, texts)
	if err := jobstest.Run(t, tested.machine.DeleteOrphansJob(), machinetranslation.SweepArguments{}); err != nil {
		t.Fatalf("orphan sweep without fields = %v, want nil", err)
	}
	texts.orphanErrors = map[string]error{"acme": localizedtext.ErrUndeclaredReference}
	if err := jobstest.Run(t, tested.machine.DeleteOrphansJob(), machinetranslation.SweepArguments{}); !jobs.IsCancel(err) {
		t.Fatalf("orphan sweep with an undeclared reference = %v, want a cancel", err)
	}
}

func TestNewValidatesSettings(t *testing.T) {
	t.Parallel()
	catalog, _ := jobstest.NewCatalog()
	if _, err := machinetranslation.New(catalog.Module("localized_texts"), machinetranslation.Settings{}); err == nil {
		t.Fatal("New without a Transactor = nil error")
	}
}
