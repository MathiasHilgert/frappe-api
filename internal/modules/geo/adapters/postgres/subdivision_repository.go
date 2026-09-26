package postgres

import (
	"context"
	"fmt"

	"github.com/jackc/pgx/v5"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// SubdivisionRepository is the Postgres application.SubdivisionReader.
type SubdivisionRepository struct {
	connection connection
}

var _ application.SubdivisionReader = (*SubdivisionRepository)(nil)

// NewSubdivisionRepository returns a SubdivisionRepository reading through
// pool.
func NewSubdivisionRepository(pool PoolSource) *SubdivisionRepository {
	return &SubdivisionRepository{connection: connection{source: pool}}
}

// Subdivisions returns a page of subdivisions ordered by id.
func (repository *SubdivisionRepository) Subdivisions(ctx context.Context, locale i18n.Locale, filter application.SubdivisionFilter) ([]domain.Subdivision, error) {
	statements := repository.connection
	return repository.collect(statements.query(ctx, listSubdivisionsQuery, statements.locale(locale), filter.After,
		statements.optional(filter.CountryCode), statements.optional(filter.ISOCode), filter.Limit))
}

// SubdivisionsByID returns the subdivisions with ids.
func (repository *SubdivisionRepository) SubdivisionsByID(ctx context.Context, locale i18n.Locale, ids []int64) ([]domain.Subdivision, error) {
	return repository.collect(repository.connection.query(ctx, subdivisionsByIDQuery, repository.connection.locale(locale), ids))
}

func (*SubdivisionRepository) collect(rows pgx.Rows, err error) ([]domain.Subdivision, error) {
	if err != nil {
		return nil, err
	}
	subdivisionRows, err := pgx.CollectRows(rows, pgx.RowToStructByName[subdivisionRow])
	if err != nil {
		return nil, fmt.Errorf("geo subdivisions: %w", err)
	}
	subdivisions := make([]domain.Subdivision, 0, len(subdivisionRows))
	for _, row := range subdivisionRows {
		subdivisions = append(subdivisions, row.subdivision())
	}
	return subdivisions, nil
}
