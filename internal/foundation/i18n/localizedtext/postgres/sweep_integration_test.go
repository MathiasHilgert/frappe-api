//go:build integration

package postgres_test

import (
	"context"
	"slices"
	"testing"

	"github.com/google/uuid"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
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
