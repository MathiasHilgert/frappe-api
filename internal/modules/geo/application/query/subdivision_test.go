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

func TestGetSubdivisionReturnsItOrNotFound(t *testing.T) {
	t.Parallel()
	handler := query.NewGetSubdivisionHandler(newFakeReaders())

	subdivision, err := handler.Handle(context.Background(), query.GetSubdivision{Locale: spanish, ID: cordobaProvince})
	if err != nil || *subdivision.ISOCode != "AR-X" {
		t.Fatalf("Handle = %+v, %v", subdivision, err)
	}
	if _, err := handler.Handle(context.Background(), query.GetSubdivision{ID: 1}); !errors.Is(err, domain.ErrNotFound) {
		t.Fatalf("missing error = %v, want domain.ErrNotFound", err)
	}
}

func TestListSubdivisionsPassesTheFilter(t *testing.T) {
	t.Parallel()
	readers := newFakeReaders()
	filter := application.SubdivisionFilter{CountryCode: "AR", ISOCode: "AR-X", After: 5, Limit: 11}

	subdivisions, err := query.NewListSubdivisionsHandler(readers).Handle(context.Background(), query.ListSubdivisions{Locale: spanish, Filter: filter})
	if err != nil || len(subdivisions) != 1 || readers.subdivisionFilter != filter {
		t.Fatalf("Handle = %+v, %v (filter %+v)", subdivisions, err, readers.subdivisionFilter)
	}
}

func TestFindSubdivisionsBatchesDistinctIDs(t *testing.T) {
	t.Parallel()
	readers := newFakeReaders()

	found, err := query.NewFindSubdivisionsHandler(readers).Handle(context.Background(),
		query.FindSubdivisions{Locale: spanish, IDs: []int64{cordobaProvince, 1, cordobaProvince}})
	if err != nil || len(found) != 1 || found[cordobaProvince].Name != "Córdoba" {
		t.Fatalf("Handle = %+v, %v", found, err)
	}
	if readers.calls["SubdivisionsByID"] != 1 || !slices.Equal(readers.lastIDs, []int64{cordobaProvince, 1}) {
		t.Fatalf("read %d times with %v", readers.calls["SubdivisionsByID"], readers.lastIDs)
	}
}
