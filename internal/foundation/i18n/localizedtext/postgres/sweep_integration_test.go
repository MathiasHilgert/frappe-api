//go:build integration

package postgres_test

import (
	"context"
	"slices"
	"testing"
	"time"

	"github.com/google/uuid"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext/postgres"
)

func TestIntegrationTenantsListsEveryTenantWithTextsAcrossRowLevelSecurity(t *testing.T) {
	tested := newSubject(t, false)
	tested.create(t, acme, "Milanesa")
	tested.create(t, globex, "Empanada")
	if _, err := tested.owner.Exec(context.Background(), `INSERT INTO localized_texts (id, source_locale, source_value, source_hash)
		VALUES ($1, 'es-419', 'Global', 'hash')`, uuid.New()); err != nil {
		t.Fatalf("insert global text: %v", err)
	}
	// An empty tenant: the sweeper lists tenants before it knows any.
	var tenants []string
	tested.mustWithin(t, "", func(ctx context.Context) error {
		var err error
		tenants, err = tested.service.Tenants(ctx)
		return err
	})
	if !slices.Equal(tenants, []string{acme, globex}) {
		t.Fatalf("Tenants = %v, want [acme globex] (global texts have no tenant)", tenants)
	}
}

func TestIntegrationReferencingFindsTheReferencingField(t *testing.T) {
	tested := newSubject(t, false)
	referenced := tested.create(t, acme, "Milanesa")
	unreferenced := tested.create(t, acme, "Empanada")
	tested.mustWithin(t, acme, func(ctx context.Context) error {
		transaction, _ := database.TransactionFromContext(ctx)
		_, err := transaction.Exec(ctx, "INSERT INTO dishes (id, description_text_id) VALUES ($1, $2)", uuid.New(), uuid.UUID(referenced))
		return err
	})
	references := []localizedtext.Reference{{Table: "dishes", Column: "description_text_id"}}
	tested.mustWithin(t, acme, func(ctx context.Context) error {
		found, err := postgres.NewStore().Referencing(ctx, references, []localizedtext.ID{referenced, unreferenced})
		if err != nil {
			return err
		}
		if len(found) != 1 || found[referenced] != references[0] {
			t.Errorf("Referencing = %+v, want only %s referenced by dishes", found, referenced)
		}
		return nil
	})
}

func TestIntegrationTheTenantDirectoryIsOnlyReadableThroughItsFunction(t *testing.T) {
	tested := newSubject(t, false)
	tested.create(t, acme, "Milanesa")
	err := tested.within(t, acme, func(ctx context.Context) error {
		transaction, _ := database.TransactionFromContext(ctx)
		_, err := transaction.Exec(ctx, "SELECT tenant_id FROM localized_text_tenant_directory")
		return err
	})
	if err == nil {
		t.Fatal("the application role read the tenant directory directly")
	}
	var policies int
	if err := tested.owner.QueryRow(context.Background(),
		"SELECT count(*) FROM pg_policies WHERE tablename = 'localized_texts' AND 'frappe_migration' = ANY (roles) AND qual <> '(tenant_id IS NULL)'").Scan(&policies); err != nil {
		t.Fatal(err)
	}
	if policies != 0 {
		t.Fatalf("frappe_migration has %d policies reaching tenant rows, want none", policies)
	}
}

func TestIntegrationLeasedAndFailedTranslationsAreNotRequestedAgain(t *testing.T) {
	tested := newSubject(t, true)
	leased := tested.create(t, acme, "Locro")
	failed := tested.create(t, acme, "Humita")
	tested.exec(t, acme, "UPDATE localized_text_translations SET requested_at = now() - interval '1 hour' WHERE text_id = ANY($1::uuid[])",
		[]uuid.UUID{uuid.UUID(leased), uuid.UUID(failed)})
	tested.mustWithin(t, acme, func(ctx context.Context) error {
		expired, err := tested.service.ExpiredPending(ctx, 10)
		if err == nil && (len(expired) != 4 || expired[0].Attempts != 1) {
			t.Errorf("ExpiredPending = %+v, want 4 requests with one attempt each", expired)
		}
		if err != nil {
			return err
		}
		if err := tested.service.LeasePending(ctx, english, []localizedtext.ID{leased}, time.Now().Add(time.Hour)); err != nil {
			return err
		}
		if err := tested.service.LeasePending(ctx, french, []localizedtext.ID{leased}, time.Now().Add(time.Hour)); err != nil {
			return err
		}
		if err := tested.service.MarkFailed(ctx, english, []localizedtext.ID{failed}); err != nil {
			return err
		}
		return tested.service.MarkFailed(ctx, french, []localizedtext.ID{failed})
	})
	before := len(tested.requester.requests)
	tested.mustWithin(t, acme, func(ctx context.Context) error {
		expired, err := tested.service.ExpiredPending(ctx, 10)
		if err == nil && len(expired) != 0 {
			t.Errorf("ExpiredPending after lease and failure = %+v, want none", expired)
		}
		_, err = tested.texts.LocalizeMany(ctx, []localizedtext.ID{leased, failed}, english)
		return err
	})
	if got := tested.requester.requests[before:]; len(got) != 0 {
		t.Fatalf("requests after lease and failure = %+v, want none", got)
	}
	if translation, _ := tested.get(t, acme, failed).Translation(english); translation.Status != localizedtext.StatusFailed {
		t.Fatalf("failed translation = %+v", translation)
	}
	tested.mustWithin(t, acme, func(ctx context.Context) error {
		return tested.texts.UpdateSource(ctx, failed, localizedtext.Source{Value: "Humita en chala"})
	})
	requested := map[i18n.Locale]bool{}
	for _, request := range tested.requester.requests[before:] {
		if request.TextID == failed {
			requested[request.Locale] = true
		}
	}
	if !requested[english] || !requested[french] {
		t.Fatalf("requests after a source change = %+v, want en and fr again", tested.requester.requests[before:])
	}
}
