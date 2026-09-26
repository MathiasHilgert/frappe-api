//go:build integration

package postgres_test

import (
	"context"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/adapters/postgres"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
)

func TestIntegrationSubdivisionRepositoryLocalizesWithFallback(t *testing.T) {
	t.Parallel()
	repository := postgres.NewSubdivisionRepository(newReadyPool(t))

	for locale, want := range map[string]string{"ja": "コルドバ州", "pt-BR": "Córdova", "": "Córdoba"} {
		var requested i18n.Locale
		if locale != "" {
			requested = i18n.MustParseLocale(locale)
		}
		subdivisions, err := repository.SubdivisionsByID(context.Background(), requested, []int64{cordobaProvince})
		if err != nil || len(subdivisions) != 1 {
			t.Fatalf("SubdivisionsByID(%s) = %+v, %v", locale, subdivisions, err)
		}
		if subdivisions[0].Name != want || *subdivisions[0].ISOCode != "AR-X" || subdivisions[0].CountryCode != "AR" {
			t.Fatalf("AR-X in %q = %+v, want name %q", locale, subdivisions[0], want)
		}
	}
}

func TestIntegrationSubdivisionRepositoryFiltersAndPages(t *testing.T) {
	t.Parallel()
	repository := postgres.NewSubdivisionRepository(newReadyPool(t))
	ctx := context.Background()

	byCode, err := repository.Subdivisions(ctx, spanish, application.SubdivisionFilter{CountryCode: "AR", ISOCode: "AR-X", Limit: 10})
	if err != nil || len(byCode) != 1 || byCode[0].ID != cordobaProvince {
		t.Fatalf("AR-X = %+v, %v", byCode, err)
	}
	argentina, err := repository.Subdivisions(ctx, spanish, application.SubdivisionFilter{CountryCode: "AR", Limit: 100})
	if err != nil || len(argentina) != 24 {
		t.Fatalf("Argentina has %d subdivisions (%v), want 24", len(argentina), err)
	}
	for index, subdivision := range argentina {
		if subdivision.CountryCode != "AR" || (index > 0 && subdivision.ID <= argentina[index-1].ID) {
			t.Fatalf("subdivision %d = %+v, want AR ordered by id", index, subdivision)
		}
	}
	next, err := repository.Subdivisions(ctx, spanish, application.SubdivisionFilter{CountryCode: "AR", After: argentina[22].ID, Limit: 10})
	if err != nil || len(next) != 1 || next[0].ID != argentina[23].ID {
		t.Fatalf("page after the 23rd = %+v, %v", next, err)
	}
	unfiltered, err := repository.Subdivisions(ctx, spanish, application.SubdivisionFilter{Limit: 2})
	if err != nil || len(unfiltered) != 2 {
		t.Fatalf("unfiltered = %+v, %v", unfiltered, err)
	}
}
