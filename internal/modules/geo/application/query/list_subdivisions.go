package query

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// ListSubdivisions asks for one page of subdivisions ordered by id.
type ListSubdivisions struct {
	Locale i18n.Locale
	Filter application.SubdivisionFilter
}

// ListSubdivisionsHandler returns a page of subdivisions.
type ListSubdivisionsHandler struct {
	subdivisions application.SubdivisionReader
}

// NewListSubdivisionsHandler returns a ListSubdivisionsHandler reading
// subdivisions.
func NewListSubdivisionsHandler(subdivisions application.SubdivisionReader) *ListSubdivisionsHandler {
	return &ListSubdivisionsHandler{subdivisions: subdivisions}
}

// Handle returns the page query.Filter selects.
func (handler *ListSubdivisionsHandler) Handle(ctx context.Context, query ListSubdivisions) ([]domain.Subdivision, error) {
	return handler.subdivisions.Subdivisions(ctx, query.Locale, query.Filter)
}
