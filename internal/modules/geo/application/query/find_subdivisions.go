package query

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// FindSubdivisions asks for the subdivisions with IDs, for example the
// subdivisions a page of cities expands.
type FindSubdivisions struct {
	Locale i18n.Locale
	IDs    []int64
}

// FindSubdivisionsHandler returns the subdivisions that exist, by id, with
// one read for the whole batch (none for no ids).
type FindSubdivisionsHandler struct {
	subdivisions application.SubdivisionReader
}

// NewFindSubdivisionsHandler returns a FindSubdivisionsHandler reading
// subdivisions.
func NewFindSubdivisionsHandler(subdivisions application.SubdivisionReader) *FindSubdivisionsHandler {
	return &FindSubdivisionsHandler{subdivisions: subdivisions}
}

// Handle returns the found subdivisions by id.
func (handler *FindSubdivisionsHandler) Handle(ctx context.Context, query FindSubdivisions) (map[int64]domain.Subdivision, error) {
	result := map[int64]domain.Subdivision{}
	ids := distinct[int64](query.IDs).values()
	if len(ids) == 0 {
		return result, nil
	}
	subdivisions, err := handler.subdivisions.SubdivisionsByID(ctx, query.Locale, ids)
	if err != nil {
		return nil, err
	}
	for _, subdivision := range subdivisions {
		result[subdivision.ID] = subdivision
	}
	return result, nil
}
