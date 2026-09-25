//go:build integration

package postgres_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/google/uuid"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext/postgres"
)

func (tested subject) exec(t *testing.T, tenant, statement string, arguments ...any) {
	t.Helper()
	tested.mustWithin(t, tenant, func(ctx context.Context) error {
		transaction, _ := database.TransactionFromContext(ctx)
		_, err := transaction.Exec(ctx, statement, arguments...)
		return err
	})
}

func TestIntegrationExpiredPendingTranslationsAreRequestedAgain(t *testing.T) {
	t.Parallel()
	tested := newSubject(t, true)
	id := tested.create(t, acme, "Locro")
	if translation, _ := tested.get(t, acme, id).Translation(english); translation.Attempts != 1 || translation.RequestedAt.IsZero() {
		t.Fatalf("en after Create = %+v, want one attempt with a request time", translation)
	}
	tested.exec(t, acme, "UPDATE localized_text_translations SET requested_at = now() - interval '1 hour' WHERE text_id = $1 AND locale = 'en'", uuid.UUID(id))

	tested.mustWithin(t, acme, func(ctx context.Context) error {
		expired, err := tested.service.ExpiredPending(ctx, 10)
		if err != nil || len(expired) != 1 || expired[0].TextID != id || expired[0].Locale != english {
			t.Errorf("ExpiredPending = %+v, %v; want the expired en request", expired, err)
		}
		return err
	})

	before := len(tested.requester.requests)
	tested.mustWithin(t, acme, func(ctx context.Context) error {
		_, err := tested.texts.Localize(ctx, id, english)
		return err
	})
	requests := tested.requester.requests[before:]
	if len(requests) != 1 || requests[0].Locale != english {
		t.Fatalf("requests after an expired pending read = %+v, want en again", requests)
	}
	translation, _ := tested.get(t, acme, id).Translation(english)
	if translation.Attempts != 2 || time.Since(translation.RequestedAt) > time.Minute {
		t.Fatalf("en after the re-request = %+v, want attempt 2 requested just now", translation)
	}
}

// abortingRequester fails with a database error, which aborts the
// surrounding transaction unless the Service isolates it.
type abortingRequester struct{}

func (abortingRequester) RequestTranslations(ctx context.Context, _ []localizedtext.TranslationRequest) error {
	transaction, _ := database.TransactionFromContext(ctx)
	_, err := transaction.Exec(ctx, "SELECT 1 / 0")
	return err
}

func TestIntegrationReadsSurviveAFailingRequesterInsideTheTransaction(t *testing.T) {
	t.Parallel()
	tested := newSubject(t, false)
	id := tested.create(t, acme, "Chipa")
	service, err := localizedtext.NewService(localizedtext.Settings{
		Store: postgres.NewStore(), Locales: newCatalog(t), Requester: abortingRequester{},
	})
	if err != nil {
		t.Fatalf("NewService: %v", err)
	}
	texts, err := service.Field(dishDescription)
	if err != nil {
		t.Fatalf("Field: %v", err)
	}
	tested.mustWithin(t, acme, func(ctx context.Context) error {
		localized, err := texts.Localize(ctx, id, english)
		if err != nil || localized.Value != "Chipa" {
			t.Errorf("Localize with a failing requester = %+v, %v; want the source", localized, err)
		}
		// The transaction is still usable after the failed request.
		_, err = texts.Get(ctx, id)
		return err
	})
}

func TestIntegrationGlobalTextsReportReadOnly(t *testing.T) {
	t.Parallel()
	tested := newSubject(t, false)
	id := localizedtext.NewID()
	if _, err := tested.owner.Exec(context.Background(), `INSERT INTO localized_texts (id, source_locale, source_value, source_hash) VALUES ($1, 'es-419', 'Bar', $2)`,
		uuid.UUID(id), localizedtext.Hash(spanish, "Bar")); err != nil {
		t.Fatalf("owner inserts a global text: %v", err)
	}
	tested.mustWithin(t, acme, func(ctx context.Context) error {
		if err := tested.texts.SetManualTranslation(ctx, id, english, "Bar"); !errors.Is(err, localizedtext.ErrReadOnlyText) {
			t.Errorf("SetManualTranslation(global) error = %v, want ErrReadOnlyText", err)
		}
		if err := tested.texts.UpdateSource(ctx, id, localizedtext.Source{Value: "Pub"}); !errors.Is(err, localizedtext.ErrReadOnlyText) {
			t.Errorf("UpdateSource(global) error = %v, want ErrReadOnlyText", err)
		}
		return nil
	})
}

func TestIntegrationUpdateSourceKeepsTheContextUnlessCleared(t *testing.T) {
	t.Parallel()
	tested := newSubject(t, false)
	var id localizedtext.ID
	tested.mustWithin(t, acme, func(ctx context.Context) error {
		var err error
		id, err = tested.texts.Create(ctx, localizedtext.Source{Value: "Mate", Context: "Beverage"})
		return err
	})
	tested.mustWithin(t, acme, func(ctx context.Context) error {
		return tested.texts.UpdateSource(ctx, id, localizedtext.Source{Value: "Mate cocido"})
	})
	if text := tested.get(t, acme, id); text.Context != "Beverage" {
		t.Fatalf("Context after an update without one = %q, want it kept", text.Context)
	}
	tested.mustWithin(t, acme, func(ctx context.Context) error {
		return tested.texts.UpdateSource(ctx, id, localizedtext.Source{Value: "Mate cocido", ClearContext: true})
	})
	if text := tested.get(t, acme, id); text.Context != "" {
		t.Fatalf("Context after ClearContext = %q, want empty", text.Context)
	}
}

func TestIntegrationRerequestedStaleTranslationsCarryTheCurrentHash(t *testing.T) {
	t.Parallel()
	tested := newSubject(t, true)
	id := tested.create(t, acme, "Asado")
	tested.mustWithin(t, acme, func(ctx context.Context) error {
		_, err := tested.service.SetMachineTranslation(ctx, id, english, "Barbecue", localizedtext.Hash(spanish, "Asado"))
		return err
	})
	tested.mustWithin(t, acme, func(ctx context.Context) error {
		return tested.texts.UpdateSource(ctx, id, localizedtext.Source{Value: "Asado de tira"})
	})
	translation, _ := tested.get(t, acme, id).Translation(english)
	if translation.Status != localizedtext.StatusPending || translation.SourceHash != localizedtext.Hash(spanish, "Asado de tira") {
		t.Fatalf("en = %+v, want pending for the current hash", translation)
	}
}

func TestIntegrationManualTranslationWaitsForAConcurrentSourceChange(t *testing.T) {
	t.Parallel()
	tested := newSubject(t, false)
	id := tested.create(t, acme, "Dulce de leche")
	newHash := localizedtext.Hash(spanish, "Dulce de leche casero")

	updating := make(chan struct{})
	release := make(chan struct{})
	updated := make(chan error, 1)
	go func() {
		updated <- tested.within(t, acme, func(ctx context.Context) error {
			if err := tested.texts.UpdateSource(ctx, id, localizedtext.Source{Value: "Dulce de leche casero"}); err != nil {
				return err
			}
			close(updating)
			<-release
			return nil
		})
	}()
	<-updating
	translated := make(chan error, 1)
	go func() {
		translated <- tested.within(t, acme, func(ctx context.Context) error {
			return tested.texts.SetManualTranslation(ctx, id, english, "Milk caramel")
		})
	}()
	time.Sleep(300 * time.Millisecond) // let the translation block on the text lock
	close(release)
	if err := <-updated; err != nil {
		t.Fatalf("UpdateSource: %v", err)
	}
	if err := <-translated; err != nil {
		t.Fatalf("SetManualTranslation: %v", err)
	}
	translation, _ := tested.get(t, acme, id).Translation(english)
	if translation.Status != localizedtext.StatusCurrent || translation.SourceHash != newHash {
		t.Fatalf("en = %+v, want current for the new source hash", translation)
	}
}

func TestIntegrationDeleteOrphansSkipsTextsBeingReferenced(t *testing.T) {
	t.Parallel()
	tested := newSubject(t, false)
	id := tested.create(t, acme, "Provoleta")

	referencing := make(chan struct{})
	release := make(chan struct{})
	referenced := make(chan error, 1)
	go func() {
		referenced <- tested.within(t, acme, func(ctx context.Context) error {
			transaction, _ := database.TransactionFromContext(ctx)
			if _, err := transaction.Exec(ctx, "INSERT INTO dishes (id, description_text_id) VALUES ($1, $2)", uuid.New(), uuid.UUID(id)); err != nil {
				return err
			}
			close(referencing)
			<-release
			return nil
		})
	}()
	<-referencing
	tested.mustWithin(t, acme, func(ctx context.Context) error {
		deleted, err := tested.service.DeleteOrphans(ctx, 0, 10)
		if err != nil || deleted != 0 {
			t.Errorf("DeleteOrphans while a reference is being committed = %d, %v; want 0, nil", deleted, err)
		}
		return err
	})
	close(release)
	if err := <-referenced; err != nil {
		t.Fatalf("referencing transaction: %v", err)
	}
	tested.get(t, acme, id)
}

func TestIntegrationDeleteOrphansValidatesForeignKeys(t *testing.T) {
	t.Parallel()
	tested := newSubject(t, false)
	if _, err := tested.owner.Exec(context.Background(), `CREATE TABLE menus (
		id uuid PRIMARY KEY,
		title_text_id uuid REFERENCES localized_texts (id) ON DELETE CASCADE
	)`); err != nil {
		t.Fatalf("create menus: %v", err)
	}
	if err := tested.within(t, acme, func(ctx context.Context) error {
		_, err := tested.service.DeleteOrphans(ctx, 0, 10)
		return err
	}); !errors.Is(err, localizedtext.ErrUnsafeForeignKey) {
		t.Fatalf("DeleteOrphans with a cascading foreign key error = %v, want ErrUnsafeForeignKey", err)
	}
	if _, err := tested.owner.Exec(context.Background(), `ALTER TABLE menus DROP CONSTRAINT menus_title_text_id_fkey,
		ADD FOREIGN KEY (title_text_id) REFERENCES localized_texts (id)`); err != nil {
		t.Fatalf("alter menus: %v", err)
	}
	if err := tested.within(t, acme, func(ctx context.Context) error {
		_, err := tested.service.DeleteOrphans(ctx, 0, 10)
		return err
	}); !errors.Is(err, localizedtext.ErrUndeclaredReference) {
		t.Fatalf("DeleteOrphans with an undeclared foreign key error = %v, want ErrUndeclaredReference", err)
	}
}

func TestIntegrationCheckLocalesReportsMissingLocales(t *testing.T) {
	t.Parallel()
	tested := newSubject(t, false)
	ctx := context.Background()
	if err := postgres.CheckLocales(ctx, tested.application, []i18n.Locale{spanish, english}); err != nil {
		t.Fatalf("CheckLocales(seeded) = %v, want nil", err)
	}
	if err := postgres.CheckLocales(ctx, tested.application, []i18n.Locale{spanish, i18n.MustParseLocale("he")}); !errors.Is(err, postgres.ErrMissingLocales) {
		t.Fatalf("CheckLocales(he) error = %v, want ErrMissingLocales", err)
	}
}

func newCatalog(t *testing.T) *i18n.Catalog {
	t.Helper()
	catalog, err := i18n.NewCatalog(i18n.Settings{Source: spanish, Supported: []i18n.Locale{spanish, english, french}, Messages: i18n.EmbeddedMessages})
	if err != nil {
		t.Fatalf("NewCatalog: %v", err)
	}
	return catalog
}
