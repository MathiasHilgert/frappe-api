//go:build integration

package postgres_test

import (
	"context"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/adapters/postgres"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
)

func TestIntegrationTimeZoneRepositoryPagesAndFinds(t *testing.T) {
	t.Parallel()
	repository := postgres.NewTimeZoneRepository(newReadyPool(t))
	ctx := context.Background()

	first, err := repository.TimeZones(ctx, application.TimeZoneFilter{After: "America/Argentina/Buenos_Aires", Limit: 2})
	if err != nil || len(first) != 2 || first[0].ID != "America/Argentina/Catamarca" || first[1].ID != "America/Argentina/Cordoba" {
		t.Fatalf("time zones = %+v, %v", first, err)
	}
	if first[1].CountryCode == nil || *first[1].CountryCode != "AR" {
		t.Fatalf("Cordoba country = %v", first[1].CountryCode)
	}
	byID, err := repository.TimeZonesByID(ctx, []string{"America/Argentina/Cordoba", "Mars/Olympus_Mons"})
	if err != nil || len(byID) != 1 {
		t.Fatalf("TimeZonesByID = %+v, %v", byID, err)
	}
}
