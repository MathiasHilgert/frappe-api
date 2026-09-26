package postgres

import (
	"context"
	"fmt"

	"github.com/jackc/pgx/v5"

	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
)

// DataRevisionRepository is the Postgres application.DataRevisionReader.
type DataRevisionRepository struct {
	connection connection
}

var _ application.DataRevisionReader = (*DataRevisionRepository)(nil)

// NewDataRevisionRepository returns a DataRevisionRepository reading
// through pool.
func NewDataRevisionRepository(pool PoolSource) *DataRevisionRepository {
	return &DataRevisionRepository{connection: connection{source: pool}}
}

// DataRevision returns the snapshot revision.
func (repository *DataRevisionRepository) DataRevision(ctx context.Context) (string, error) {
	rows, err := repository.connection.query(ctx, dataRevisionQuery)
	if err != nil {
		return "", err
	}
	revision, err := pgx.CollectExactlyOneRow(rows, pgx.RowTo[string])
	if err != nil {
		return "", fmt.Errorf("geo data revision: %w", err)
	}
	return revision, nil
}
