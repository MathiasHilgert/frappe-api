//go:build integration

package dependencies

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"sync"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/database/databasetest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs"
)

const flowTenant = "acme"

// fakeDeepL answers /v2/translate with "<target>:<text>", holding back
// any text listed in blocked until its channel is closed.
type fakeDeepL struct {
	blocked     map[string]chan struct{}
	translated  map[string]int
	received    []string
	rateLimited int
	mutex       sync.Mutex
}

func (fake *fakeDeepL) ServeHTTP(writer http.ResponseWriter, request *http.Request) {
	var body struct {
		TargetLanguage string   `json:"target_lang"`
		Text           []string `json:"text"`
	}
	if request.Header.Get("Authorization") != "DeepL-Auth-Key flow-key:fx" || json.NewDecoder(request.Body).Decode(&body) != nil {
		writer.WriteHeader(http.StatusForbidden)
		return
	}
	fake.mutex.Lock()
	fake.received = append(fake.received, body.Text...)
	if fake.rateLimited > 0 {
		fake.rateLimited--
		fake.mutex.Unlock()
		writer.Header().Set("Retry-After", "1")
		writer.WriteHeader(http.StatusTooManyRequests)
		return
	}
	if fake.translated == nil {
		fake.translated = map[string]int{}
	}
	for _, text := range body.Text {
		fake.translated[text]++
	}
	fake.mutex.Unlock()
	type translation struct {
		Text string `json:"text"`
	}
	response := struct {
		Translations []translation `json:"translations"`
	}{}
	for _, text := range body.Text {
		if gate, ok := fake.blocked[text]; ok {
			<-gate
		}
		response.Translations = append(response.Translations, translation{Text: body.TargetLanguage + ":" + text})
	}
	_ = json.NewEncoder(writer).Encode(response)
}

type translationFlow struct {
	pool  *pgxpool.Pool
	texts *localizedtext.FieldTexts
}

func newTranslationFlow(t *testing.T, fake *fakeDeepL, mutate ...func(*configuration.Configuration)) translationFlow {
	t.Helper()
	server := httptest.NewServer(fake)
	t.Cleanup(server.Close)
	owner, pool := databasetest.NewWithOwner(t)
	// The declared field's table, so the sweeps find its foreign key.
	if _, err := owner.Exec(context.Background(), `CREATE TABLE dishes (
		id uuid PRIMARY KEY,
		description_text_id uuid REFERENCES localized_texts (id)
	)`); err != nil {
		t.Fatalf("create dishes: %v", err)
	}
	instance := application.New(application.WithHookTimeout(10 * time.Second))
	poolHandle := application.Provide(instance, application.Dependency[*pgxpool.Pool]{
		Name: "database",
		Up:   func(context.Context) (*pgxpool.Pool, error) { return pool, nil },
	})
	catalog := jobs.NewCatalog()
	loaded := configuration.Configuration{
		Internationalization: configuration.Internationalization{SourceLocale: "es-419", SupportedLocales: []string{"es-419", "en", "fr"}},
		DeepL: configuration.DeepL{
			APIKey: "flow-key:fx", BaseURL: server.URL, EnglishVariant: "EN-GB", Timeout: 10 * time.Second, BatchSize: 50,
		},
		LocalizedTexts: configuration.LocalizedTexts{ExpiredSweepInterval: time.Hour, OrphanSweepInterval: time.Hour},
	}
	for _, change := range mutate {
		change(&loaded)
	}
	_, service, err := provideInternationalization(loaded, catalog.Module(localizedTextsJobsModule), poolHandle)
	if err != nil {
		t.Fatalf("provideInternationalization: %v", err)
	}
	texts, err := service.Field(localizedtext.Field{Table: "dishes", Column: "description_text_id", Context: "Description of a dish"})
	if err != nil {
		t.Fatalf("Field: %v", err)
	}
	provideJobs(instance, configuration.Jobs{
		Enabled: true, Workers: 4, MaxAttempts: 3, FetchPollInterval: 50 * time.Millisecond,
		JobTimeout: time.Minute, CompletedRetention: time.Hour, MetricsInterval: time.Second,
	}, catalog, poolHandle)
	if err := instance.Up(context.Background()); err != nil {
		t.Fatalf("Up: %v", err)
	}
	t.Cleanup(func() {
		for gate := range fake.blocked {
			select {
			case <-fake.blocked[gate]:
			default:
				close(fake.blocked[gate])
			}
		}
		_ = instance.Down(context.Background())
	})
	return translationFlow{pool: pool, texts: texts}
}

func (flow translationFlow) within(t *testing.T, work func(ctx context.Context) error) {
	t.Helper()
	err := database.WithinTransaction(context.Background(), flow.pool, database.TransactionSettings{"application.tenant": flowTenant},
		func(ctx context.Context, _ pgx.Tx) error { return work(ctx) })
	if err != nil {
		t.Fatalf("transaction: %v", err)
	}
}

func (flow translationFlow) create(t *testing.T, value string) localizedtext.ID {
	t.Helper()
	var id localizedtext.ID
	flow.within(t, func(ctx context.Context) error {
		var err error
		id, err = flow.texts.Create(ctx, localizedtext.Source{Value: value})
		return err
	})
	return id
}

func (flow translationFlow) translation(t *testing.T, id localizedtext.ID, locale i18n.Locale) localizedtext.Translation {
	t.Helper()
	var translation localizedtext.Translation
	flow.within(t, func(ctx context.Context) error {
		text, err := flow.texts.Get(ctx, id)
		translation, _ = text.Translation(locale)
		return err
	})
	return translation
}

// eventually waits for the translation of id into locale to satisfy done.
func (flow translationFlow) eventually(t *testing.T, id localizedtext.ID, locale i18n.Locale, done func(localizedtext.Translation) bool) localizedtext.Translation {
	t.Helper()
	deadline := time.Now().Add(30 * time.Second)
	for {
		translation := flow.translation(t, id, locale)
		if done(translation) {
			return translation
		}
		if time.Now().After(deadline) {
			rows, _ := flow.pool.Query(context.Background(), "SELECT kind, state, args::text, coalesce(errors::text, ''), metadata::text FROM river_job ORDER BY id")
			for rows.Next() {
				var kind, state, args, errs, metadata string
				_ = rows.Scan(&kind, &state, &args, &errs, &metadata)
				t.Logf("job %s %s %s %s %s", kind, state, args, errs, metadata)
			}
			rows.Close()
			t.Fatalf("translation into %s = %+v; condition never met", locale, translation)
		}
		time.Sleep(100 * time.Millisecond)
	}
}

// idle waits until no translate job is left unfinished.
func (flow translationFlow) idle(t *testing.T) {
	t.Helper()
	deadline := time.Now().Add(30 * time.Second)
	for {
		var unfinished int
		if err := flow.pool.QueryRow(context.Background(), `SELECT count(*) FROM river_job
			WHERE kind = 'localized_texts.machine_translate' AND state NOT IN ('completed', 'cancelled', 'discarded')`).Scan(&unfinished); err != nil {
			t.Fatal(err)
		}
		if unfinished == 0 {
			return
		}
		if time.Now().After(deadline) {
			t.Fatalf("%d translate jobs still unfinished", unfinished)
		}
		time.Sleep(100 * time.Millisecond)
	}
}

func machineCurrent(value string) func(localizedtext.Translation) bool {
	return func(translation localizedtext.Translation) bool {
		return translation.Origin == localizedtext.OriginMachine && translation.Status == localizedtext.StatusCurrent && translation.Value == value
	}
}

func TestIntegrationMachineTranslationFlow(t *testing.T) {
	english, french := i18n.MustParseLocale("en"), i18n.MustParseLocale("fr")
	manualGate, sourceGate := make(chan struct{}), make(chan struct{})
	fake := &fakeDeepL{blocked: map[string]chan struct{}{"Manual": manualGate, "Mid": sourceGate}}
	flow := newTranslationFlow(t, fake)

	t.Run("a new text is translated and stored as a current machine translation", func(t *testing.T) {
		id := flow.create(t, "Milanesa")
		flow.eventually(t, id, english, machineCurrent("EN-GB:Milanesa"))
		flow.eventually(t, id, french, machineCurrent("FR:Milanesa"))
	})

	t.Run("localizing a missing locale enqueues its translation", func(t *testing.T) {
		id := flow.create(t, "Empanada")
		flow.eventually(t, id, french, machineCurrent("FR:Empanada"))
		flow.within(t, func(ctx context.Context) error {
			transaction, _ := database.TransactionFromContext(ctx)
			_, err := transaction.Exec(ctx, "DELETE FROM localized_text_translations WHERE text_id = $1 AND locale = 'fr'", uuid.UUID(id))
			return err
		})
		flow.within(t, func(ctx context.Context) error {
			localized, err := flow.texts.Localize(ctx, id, french)
			if err == nil && (!localized.Fallback || localized.Value != "Empanada") {
				t.Errorf("Localize = %+v, want the source as fallback", localized)
			}
			return err
		})
		flow.eventually(t, id, french, machineCurrent("FR:Empanada"))
	})

	t.Run("a manual translation is never overwritten", func(t *testing.T) {
		id := flow.create(t, "Manual")
		flow.within(t, func(ctx context.Context) error {
			return flow.texts.SetManualTranslation(ctx, id, english, "Handmade")
		})
		close(manualGate)
		flow.idle(t)
		got := flow.translation(t, id, english)
		if got.Origin != localizedtext.OriginManual || got.Value != "Handmade" {
			t.Fatalf("translation = %+v, want the manual one", got)
		}
	})

	t.Run("a result for a source changed mid-flight is discarded", func(t *testing.T) {
		id := flow.create(t, "Mid")
		// Wait until DeepL holds the old source, then change it.
		deadline := time.Now().Add(30 * time.Second)
		for !fake.saw("Mid") {
			if time.Now().After(deadline) {
				t.Fatal("DeepL never received the original source")
			}
			time.Sleep(50 * time.Millisecond)
		}
		flow.within(t, func(ctx context.Context) error {
			return flow.texts.UpdateSource(ctx, id, localizedtext.Source{Value: "Mid changed"})
		})
		close(sourceGate)
		flow.idle(t)
		flow.eventually(t, id, english, machineCurrent("EN-GB:Mid changed"))
	})
}

func (fake *fakeDeepL) saw(text string) bool {
	fake.mutex.Lock()
	defer fake.mutex.Unlock()
	for _, received := range fake.received {
		if received == text {
			return true
		}
	}
	return false
}

func (fake *fakeDeepL) translations(text string) int {
	fake.mutex.Lock()
	defer fake.mutex.Unlock()
	return fake.translated[text]
}

// TestIntegrationRateLimitedTranslationsAreNotRequestedTwice keeps DeepL
// rate limiting across several expired sweep ticks (a one second pending
// timeout and sweep interval): the snoozed jobs lease their texts, so
// after recovery each text is translated exactly once per locale.
func TestIntegrationRateLimitedTranslationsAreNotRequestedTwice(t *testing.T) {
	english, french := i18n.MustParseLocale("en"), i18n.MustParseLocale("fr")
	fake := &fakeDeepL{rateLimited: 12}
	flow := newTranslationFlow(t, fake, func(loaded *configuration.Configuration) {
		loaded.LocalizedTexts.ExpiredSweepInterval = time.Second
		loaded.LocalizedTexts.PendingTimeout = time.Second
	})
	// The sweep must already tick while DeepL rate limits.
	flow.sweeps(t, 1)
	id := flow.create(t, "Tamal")
	flow.eventually(t, id, english, machineCurrent("EN-GB:Tamal"))
	flow.eventually(t, id, french, machineCurrent("FR:Tamal"))
	if sweeps := flow.sweeps(t, 4); sweeps < 4 {
		t.Fatalf("expired sweeps = %d, want several during the test", sweeps)
	}
	flow.idle(t)
	if got := fake.translations("Tamal"); got != 2 {
		t.Fatalf("successful DeepL translations of the text = %d, want 2 (en and fr once each)", got)
	}
	var jobCount int
	if err := flow.pool.QueryRow(context.Background(), "SELECT count(*) FROM river_job WHERE kind = 'localized_texts.machine_translate'").Scan(&jobCount); err != nil {
		t.Fatal(err)
	}
	if jobCount != 2 {
		t.Fatalf("translate jobs = %d, want 2 (the sweep must not request leased texts again)", jobCount)
	}
}

// sweeps waits until at least minimum expired sweeps completed and
// returns how many did.
func (flow translationFlow) sweeps(t *testing.T, minimum int) int {
	t.Helper()
	deadline := time.Now().Add(30 * time.Second)
	for {
		var count int
		if err := flow.pool.QueryRow(context.Background(), `SELECT count(*) FROM river_job
			WHERE kind = 'localized_texts.request_expired_translations' AND state = 'completed'`).Scan(&count); err != nil {
			t.Fatal(err)
		}
		if count >= minimum || time.Now().After(deadline) {
			return count
		}
		time.Sleep(100 * time.Millisecond)
	}
}
