package query

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// GetSubdivision asks for one subdivision by GeoNames id.
type GetSubdivision struct {
	Locale i18n.Locale
	ID     int64
}

// GetSubdivisionHandler returns the subdivision, or domain.ErrNotFound.
type GetSubdivisionHandler struct {
	subdivisions application.SubdivisionReader
}

// NewGetSubdivisionHandler returns a GetSubdivisionHandler reading
// subdivisions.
func NewGetSubdivisionHandler(subdivisions application.SubdivisionReader) *GetSubdivisionHandler {
	return &GetSubdivisionHandler{subdivisions: subdivisions}
}

// Handle returns the subdivision with query.ID.
func (handler *GetSubdivisionHandler) Handle(ctx context.Context, query GetSubdivision) (domain.Subdivision, error) {
	subdivisions, err := handler.subdivisions.SubdivisionsByID(ctx, query.Locale, []int64{query.ID})
	if err != nil {
		return domain.Subdivision{}, err
	}
	for _, subdivision := range subdivisions {
		if subdivision.ID == query.ID {
			return subdivision, nil
		}
	}
	return domain.Subdivision{}, domain.ErrNotFound
}
