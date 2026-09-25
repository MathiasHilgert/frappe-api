//go:build integration

package postgres_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/database/databasetest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext/postgres"
)

const (
	acme   = "acme"
	globex = "globex"
)

var (
	spanish = i18n.MustParseLocale("es-419")
	english = i18n.MustParseLocale("en")
	french  = i18n.MustParseLocale("fr")
)

var dishDescription = localizedtext.Field{
	Table:   "dishes",
	Column:  "description_text_id",
	Context: "Description of a dish on a restaurant menu",
}

type recordingRequester struct {
	requests []localizedtext.TranslationRequest
}

func (requester *recordingRequester) RequestTranslations(_ context.Context, requests []localizedtext.TranslationRequest) error {
	requester.requests = append(requester.requests, requests...)
	return nil
}

// subject is one fresh database with the Service wired on the Postgres
// store, exactly as a module would use it.
type subject struct {
	owner       *pgxpool.Pool
	application *pgxpool.Pool
	service     *localizedtext.Service
	texts       *localizedtext.FieldTexts
	requester   *recordingRequester
}

func newSubject(t *testing.T, withRequester bool) subject {
	t.Helper()
	owner, application := databasetest.NewWithOwner(t)
	catalog, err := i18n.NewCatalog(i18n.Settings{
		Source:    spanish,
		Supported: []i18n.Locale{spanish, english, french},
		Messages:  i18n.EmbeddedMessages,
	})
	if err != nil {
		t.Fatalf("NewCatalog: %v", err)
	}
	settings := localizedtext.Settings{Store: postgres.NewStore(), Locales: catalog}
	requester := &recordingRequester{}
	if withRequester {
		settings.Requester = requester
	}
	service, err := localizedtext.NewService(settings)
	if err != nil {
		t.Fatalf("NewService: %v", err)
	}
	texts, err := service.Field(dishDescription)
	if err != nil {
		t.Fatalf("Field: %v", err)
	}
	// dishes is a scratch entity showing how a module references texts.
	if _, err := owner.Exec(context.Background(), `CREATE TABLE dishes (
		id uuid PRIMARY KEY,
		description_text_id uuid REFERENCES localized_texts (id)
	)`); err != nil {
		t.Fatalf("create dishes: %v", err)
	}
	return subject{owner: owner, application: application, service: service, texts: texts, requester: requester}
}

// within runs work in a transaction of the application role for tenant.
func (subject subject) within(t *testing.T, tenant string, work func(ctx context.Context) error) error {
	t.Helper()
	return database.WithinTransaction(context.Background(), subject.application, database.TransactionSettings{
		"application.tenant": tenant,
	}, func(ctx context.Context, _ pgx.Tx) error {
		return work(ctx)
	})
}

func (subject subject) mustWithin(t *testing.T, tenant string, work func(ctx context.Context) error) {
	t.Helper()
	if err := subject.within(t, tenant, work); err != nil {
		t.Fatalf("transaction for %s: %v", tenant, err)
	}
}

func (subject subject) create(t *testing.T, tenant, value string) localizedtext.ID {
	t.Helper()
	var id localizedtext.ID
	subject.mustWithin(t, tenant, func(ctx context.Context) error {
		var err error
		id, err = subject.texts.Create(ctx, localizedtext.Source{Value: value})
		return err
	})
	return id
}

func (subject subject) get(t *testing.T, tenant string, id localizedtext.ID) localizedtext.Text {
	t.Helper()
	var text localizedtext.Text
	subject.mustWithin(t, tenant, func(ctx context.Context) error {
		var err error
		text, err = subject.texts.Get(ctx, id)
		return err
	})
	return text
}

func (subject subject) countTranslations(t *testing.T, id localizedtext.ID) int {
	t.Helper()
	var count int
	// Counted as the application role of the text's tenant: the owner is
	// subject to FORCE ROW LEVEL SECURITY and sees only global rows.
	subject.mustWithin(t, acme, func(ctx context.Context) error {
		transaction, _ := database.TransactionFromContext(ctx)
		return transaction.QueryRow(ctx, "SELECT count(*) FROM localized_text_translations WHERE text_id = $1", uuid.UUID(id)).Scan(&count)
	})
	return count
}

func TestIntegrationStoreRequiresATransaction(t *testing.T) {
	t.Parallel()
	subject := newSubject(t, false)
	if _, err := subject.texts.Create(context.Background(), localizedtext.Source{Value: "Hola"}); !errors.Is(err, postgres.ErrNoTransaction) {
		t.Fatalf("Create outside a transaction error = %v, want ErrNoTransaction", err)
	}
}

func TestIntegrationCreateAndGetStampTheTenantAndHash(t *testing.T) {
	t.Parallel()
	subject := newSubject(t, false)
	id := subject.create(t, acme, "Milanesa napolitana")
	text := subject.get(t, acme, id)
	if text.SourceValue != "Milanesa napolitana" || text.SourceLocale != spanish || text.SourceHash != localizedtext.Hash(spanish, "Milanesa napolitana") {
		t.Fatalf("Get = %+v, want the stored source with its hash", text)
	}
	var tenant string
	subject.mustWithin(t, acme, func(ctx context.Context) error {
		transaction, _ := database.TransactionFromContext(ctx)
		return transaction.QueryRow(ctx, "SELECT tenant_id FROM localized_texts WHERE id = $1", uuid.UUID(id)).Scan(&tenant)
	})
	if tenant != acme {
		t.Fatalf("tenant_id = %q, want %q taken from the transaction setting", tenant, acme)
	}
}

func TestIntegrationRowLevelSecurityIsolatesTenants(t *testing.T) {
	t.Parallel()
	subject := newSubject(t, false)
	id := subject.create(t, acme, "Solo de acme")

	subject.mustWithin(t, globex, func(ctx context.Context) error {
		if _, err := subject.texts.Get(ctx, id); !errors.Is(err, localizedtext.ErrNotFound) {
			t.Errorf("Get from another tenant error = %v, want ErrNotFound", err)
		}
		localized, err := subject.texts.LocalizeMany(ctx, []localizedtext.ID{id}, english)
		if err != nil || len(localized) != 0 {
			t.Errorf("LocalizeMany from another tenant = %v, %v; want nothing", localized, err)
		}
		if err := subject.texts.UpdateSource(ctx, id, localizedtext.Source{Value: "Robado"}); !errors.Is(err, localizedtext.ErrNotFound) {
			t.Errorf("UpdateSource from another tenant error = %v, want ErrNotFound", err)
		}
		if err := subject.texts.SetManualTranslation(ctx, id, english, "Stolen"); !errors.Is(err, localizedtext.ErrNotFound) {
			t.Errorf("SetManualTranslation from another tenant error = %v, want ErrNotFound", err)
		}
		return subject.texts.Delete(ctx, id)
	})
	if text := subject.get(t, acme, id); text.SourceValue != "Solo de acme" || len(text.Translations) != 0 {
		t.Fatalf("acme's text after another tenant's attempts = %+v, want it untouched", text)
	}

	// Without a tenant, the application role cannot create a text: a
	// tenantless (global) text is reserved to the schema owner.
	if err := subject.within(t, "", func(ctx context.Context) error {
		_, err := subject.texts.Create(ctx, localizedtext.Source{Value: "Global?"})
		return err
	}); err == nil {
		t.Fatal("the application role created a text without a tenant, want a row level security violation")
	}
}

func TestIntegrationGlobalTextsAreReadableButNotWritableByTenants(t *testing.T) {
	t.Parallel()
	subject := newSubject(t, true)
	id := localizedtext.NewID()
	ctx := context.Background()
	if _, err := subject.owner.Exec(ctx, `INSERT INTO localized_texts (id, source_locale, source_value, source_hash) VALUES ($1, 'es-419', 'Restaurante', $2)`,
		uuid.UUID(id), localizedtext.Hash(spanish, "Restaurante")); err != nil {
		t.Fatalf("owner inserts a global text: %v", err)
	}
	if _, err := subject.owner.Exec(ctx, `INSERT INTO localized_text_translations (text_id, locale, value, origin, status, source_hash) VALUES ($1, 'en', 'Restaurant', 'manual', 'current', $2)`,
		uuid.UUID(id), localizedtext.Hash(spanish, "Restaurante")); err != nil {
		t.Fatalf("owner inserts a global translation: %v", err)
	}

	subject.mustWithin(t, acme, func(ctx context.Context) error {
		localized, err := subject.texts.Localize(ctx, id, english)
		if err != nil || localized.Value != "Restaurant" {
			t.Errorf("Localize(global, en) = %+v, %v; want the global translation", localized, err)
		}
		// A missing locale of a global text falls back without trying to
		// mark it pending (which the tenant may not write).
		localized, err = subject.texts.Localize(ctx, id, french)
		if err != nil || !localized.Fallback {
			t.Errorf("Localize(global, fr) = %+v, %v; want a fallback", localized, err)
		}
		return nil
	})
	if len(subject.requester.requests) != 0 {
		t.Fatalf("requests = %+v, want none for a global text", subject.requester.requests)
	}
	if err := subject.within(t, acme, func(ctx context.Context) error {
		return subject.texts.UpdateSource(ctx, id, localizedtext.Source{Value: "Cambiado"})
	}); !errors.Is(err, localizedtext.ErrReadOnlyText) {
		t.Fatalf("tenant UpdateSource(global) error = %v, want ErrReadOnlyText", err)
	}
	if err := subject.within(t, acme, func(ctx context.Context) error {
		return subject.texts.SetManualTranslation(ctx, id, french, "Restaurant")
	}); err == nil {
		t.Fatal("a tenant translated a global text, want a row level security violation")
	}
}

func TestIntegrationUpdateSourceMarksTranslationsStaleAndKeepsManualOnes(t *testing.T) {
	t.Parallel()
	subject := newSubject(t, false)
	id := subject.create(t, acme, "Papas fritas")
	oldHash := localizedtext.Hash(spanish, "Papas fritas")
	subject.mustWithin(t, acme, func(ctx context.Context) error {
		if err := subject.texts.SetManualTranslation(ctx, id, english, "French fries"); err != nil {
			return err
		}
		_, err := subject.service.SetMachineTranslation(ctx, id, french, "Frites", oldHash)
		return err
	})

	// The same source again changes nothing.
	subject.mustWithin(t, acme, func(ctx context.Context) error {
		return subject.texts.UpdateSource(ctx, id, localizedtext.Source{Value: "Papas fritas"})
	})
	for _, translation := range subject.get(t, acme, id).Translations {
		if translation.Status != localizedtext.StatusCurrent {
			t.Fatalf("after an unchanged source, %s is %s, want current", translation.Locale, translation.Status)
		}
	}

	subject.mustWithin(t, acme, func(ctx context.Context) error {
		return subject.texts.UpdateSource(ctx, id, localizedtext.Source{Value: "Papas rusticas"})
	})
	text := subject.get(t, acme, id)
	if text.SourceHash != localizedtext.Hash(spanish, "Papas rusticas") {
		t.Fatalf("SourceHash = %q, want the new hash", text.SourceHash)
	}
	manual, _ := text.Translation(english)
	machine, _ := text.Translation(french)
	if manual.Origin != localizedtext.OriginManual || manual.Status != localizedtext.StatusStale || manual.Value != "French fries" {
		t.Errorf("manual = %+v, want manual, stale, value kept", manual)
	}
	if machine.Origin != localizedtext.OriginMachine || machine.Status != localizedtext.StatusStale || machine.Value != "Frites" {
		t.Errorf("machine = %+v, want machine, stale, value kept", machine)
	}
}

func TestIntegrationMachineTranslationsNeverOverwriteManualOnesNorOutdatedSources(t *testing.T) {
	t.Parallel()
	subject := newSubject(t, false)
	id := subject.create(t, acme, "Flan casero")
	hash := localizedtext.Hash(spanish, "Flan casero")
	subject.mustWithin(t, acme, func(ctx context.Context) error {
		if err := subject.texts.SetManualTranslation(ctx, id, english, "Homemade flan"); err != nil {
			return err
		}
		stored, err := subject.service.SetMachineTranslation(ctx, id, english, "House flan", hash)
		if err != nil || stored {
			t.Errorf("SetMachineTranslation over a manual one = %v, %v; want false, nil", stored, err)
		}
		stored, err = subject.service.SetMachineTranslation(ctx, id, french, "Flan maison", localizedtext.Hash(spanish, "older source"))
		if err != nil || stored {
			t.Errorf("SetMachineTranslation for an outdated source = %v, %v; want false, nil", stored, err)
		}
		stored, err = subject.service.SetMachineTranslation(ctx, id, french, "Flan maison", hash)
		if err != nil || !stored {
			t.Errorf("SetMachineTranslation for the current source = %v, %v; want true, nil", stored, err)
		}
		if err := subject.texts.SetManualTranslation(ctx, id, spanish, "Otro"); !errors.Is(err, localizedtext.ErrSourceLocale) {
			t.Errorf("SetManualTranslation into the source locale error = %v, want ErrSourceLocale", err)
		}
		if err := subject.texts.SetManualTranslation(ctx, id, i18n.MustParseLocale("de"), "Flan"); !errors.Is(err, localizedtext.ErrUnsupportedLocale) {
			t.Errorf("SetManualTranslation into an unsupported locale error = %v, want ErrUnsupportedLocale", err)
		}
		return nil
	})
	text := subject.get(t, acme, id)
	if english, _ := text.Translation(english); english.Value != "Homemade flan" || english.Origin != localizedtext.OriginManual {
		t.Fatalf("en = %+v, want the manual translation untouched", english)
	}
	if french, _ := text.Translation(french); french.Value != "Flan maison" || french.Origin != localizedtext.OriginMachine || french.Status != localizedtext.StatusCurrent {
		t.Fatalf("fr = %+v, want the current machine translation", french)
	}
}

func TestIntegrationLocalizeFallsBackAndRequestsOnce(t *testing.T) {
	t.Parallel()
	subject := newSubject(t, true)
	id := subject.create(t, acme, "Empanadas")
	created := len(subject.requester.requests)
	if created != 2 {
		t.Fatalf("requests after Create = %+v, want en and fr", subject.requester.requests)
	}
	pending := subject.get(t, acme, id)
	if translation, _ := pending.Translation(english); translation.Status != localizedtext.StatusPending || translation.Value != "" {
		t.Fatalf("en after Create = %+v, want pending without a value", translation)
	}

	subject.mustWithin(t, acme, func(ctx context.Context) error {
		localized, err := subject.texts.Localize(ctx, id, english)
		if err != nil || !localized.Fallback || localized.Value != "Empanadas" || localized.Locale != spanish {
			t.Errorf("Localize(en) while pending = %+v, %v; want the source as a fallback", localized, err)
		}
		return err
	})
	if len(subject.requester.requests) != created {
		t.Fatalf("requests = %+v, want no new request while pending", subject.requester.requests)
	}

	subject.mustWithin(t, acme, func(ctx context.Context) error {
		if _, err := subject.service.SetMachineTranslation(ctx, id, english, "Empanadas (pastries)", localizedtext.Hash(spanish, "Empanadas")); err != nil {
			return err
		}
		localized, err := subject.texts.Localize(ctx, id, i18n.MustParseLocale("en-GB"))
		if err != nil || localized.Fallback || localized.Value != "Empanadas (pastries)" || localized.Origin != localizedtext.OriginMachine {
			t.Errorf("Localize(en-GB) = %+v, %v; want the machine translation", localized, err)
		}
		return err
	})

	// Changing the source re-requests the stale machine translation once.
	subject.mustWithin(t, acme, func(ctx context.Context) error {
		return subject.texts.UpdateSource(ctx, id, localizedtext.Source{Value: "Empanadas saltenas", Context: "Street food"})
	})
	requests := subject.requester.requests[created:]
	if len(requests) != 2 {
		t.Fatalf("requests after UpdateSource = %+v, want en (stale) and fr (missing again)", requests)
	}
	for _, request := range requests {
		if request.SourceHash != localizedtext.Hash(spanish, "Empanadas saltenas") || request.Context != "Street food" {
			t.Errorf("request = %+v, want the new hash and the text's own context", request)
		}
	}
	if translation, _ := subject.get(t, acme, id).Translation(english); translation.Status != localizedtext.StatusPending || translation.Value != "Empanadas (pastries)" {
		t.Fatalf("en after UpdateSource = %+v, want pending, keeping the previous machine value", translation)
	}
}

func TestIntegrationLocalizeManyLoadsAListInOneCall(t *testing.T) {
	t.Parallel()
	subject := newSubject(t, false)
	first := subject.create(t, acme, "Uno")
	second := subject.create(t, acme, "Dos")
	subject.mustWithin(t, acme, func(ctx context.Context) error {
		if err := subject.texts.SetManualTranslation(ctx, first, english, "One"); err != nil {
			return err
		}
		localized, err := subject.texts.LocalizeMany(ctx, []localizedtext.ID{first, second, localizedtext.NewID()}, english)
		if err != nil {
			return err
		}
		if len(localized) != 2 || localized[first].Value != "One" || localized[second].Value != "Dos" || !localized[second].Fallback {
			t.Errorf("LocalizeMany = %+v, want One and a fallback to Dos", localized)
		}
		return nil
	})
}

func TestIntegrationDeleteCascadesToTranslations(t *testing.T) {
	t.Parallel()
	subject := newSubject(t, false)
	id := subject.create(t, acme, "Tarta")
	subject.mustWithin(t, acme, func(ctx context.Context) error {
		return subject.texts.SetManualTranslation(ctx, id, english, "Pie")
	})
	if count := subject.countTranslations(t, id); count != 1 {
		t.Fatalf("translations before Delete = %d, want 1", count)
	}
	subject.mustWithin(t, acme, func(ctx context.Context) error {
		return subject.texts.Delete(ctx, id)
	})
	if count := subject.countTranslations(t, id); count != 0 {
		t.Fatalf("translations after Delete = %d, want 0", count)
	}
}

func TestIntegrationDeleteOrphansKeepsReferencedAndOtherTenantsTexts(t *testing.T) {
	t.Parallel()
	subject := newSubject(t, false)
	referenced := subject.create(t, acme, "Referenciado")
	orphan := subject.create(t, acme, "Huerfano")
	otherTenantOrphan := subject.create(t, globex, "Huerfano de globex")
	subject.mustWithin(t, acme, func(ctx context.Context) error {
		if err := subject.texts.SetManualTranslation(ctx, orphan, english, "Orphan"); err != nil {
			return err
		}
		transaction, _ := database.TransactionFromContext(ctx)
		_, err := transaction.Exec(ctx, "INSERT INTO dishes (id, description_text_id) VALUES ($1, $2)", uuid.New(), uuid.UUID(referenced))
		return err
	})

	subject.mustWithin(t, acme, func(ctx context.Context) error {
		deleted, err := subject.service.DeleteOrphans(ctx, time.Hour, 100)
		if err != nil || deleted != 0 {
			t.Errorf("DeleteOrphans(older than an hour) = %d, %v; want 0 (grace period)", deleted, err)
		}
		deleted, err = subject.service.DeleteOrphans(ctx, 0, 100)
		if err != nil || deleted != 1 {
			t.Errorf("DeleteOrphans = %d, %v; want exactly the one orphan", deleted, err)
		}
		if _, getErr := subject.texts.Get(ctx, orphan); !errors.Is(getErr, localizedtext.ErrNotFound) {
			t.Errorf("orphan still exists: %v", getErr)
		}
		_, err = subject.texts.Get(ctx, referenced)
		return err
	})
	subject.get(t, globex, otherTenantOrphan)
}
