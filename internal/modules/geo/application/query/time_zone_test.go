package query_test

import (
	"context"
	"errors"
	"slices"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application/query"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

const cordobaZone = "America/Argentina/Cordoba"

func TestGetTimeZoneReturnsItOrNotFound(t *testing.T) {
	t.Parallel()
	handler := query.NewGetTimeZoneHandler(newFakeReaders())

	timeZone, err := handler.Handle(context.Background(), query.GetTimeZone{ID: cordobaZone})
	if err != nil || *timeZone.CountryCode != "AR" {
		t.Fatalf("Handle = %+v, %v", timeZone, err)
	}
	if _, err := handler.Handle(context.Background(), query.GetTimeZone{ID: "Mars/Olympus_Mons"}); !errors.Is(err, domain.ErrNotFound) {
		t.Fatalf("missing error = %v, want domain.ErrNotFound", err)
	}
}

func TestListTimeZonesPassesTheFilter(t *testing.T) {
	t.Parallel()
	readers := newFakeReaders()
	filter := application.TimeZoneFilter{After: "America/Argentina/Buenos_Aires", Limit: 3}

	timeZones, err := query.NewListTimeZonesHandler(readers).Handle(context.Background(), query.ListTimeZones{Filter: filter})
	if err != nil || len(timeZones) != 1 || readers.timeZoneFilter != filter {
		t.Fatalf("Handle = %+v, %v", timeZones, err)
	}
}

func TestFindTimeZonesBatchesDistinctIDs(t *testing.T) {
	t.Parallel()
	readers := newFakeReaders()

	found, err := query.NewFindTimeZonesHandler(readers).Handle(context.Background(), query.FindTimeZones{IDs: []string{cordobaZone, cordobaZone, "Etc/UTC"}})
	if err != nil || len(found) != 1 || found[cordobaZone].ID != cordobaZone {
		t.Fatalf("Handle = %+v, %v", found, err)
	}
	if readers.calls["TimeZonesByID"] != 1 || !slices.Equal(readers.lastTimeZoneIDs, []string{cordobaZone, "Etc/UTC"}) {
		t.Fatalf("read %d times with %v", readers.calls["TimeZonesByID"], readers.lastTimeZoneIDs)
	}
}
