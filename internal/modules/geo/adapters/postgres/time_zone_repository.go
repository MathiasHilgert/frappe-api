package postgres

import (
	"context"
	"fmt"

	"github.com/jackc/pgx/v5"

	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// TimeZoneRepository is the Postgres application.TimeZoneReader.
type TimeZoneRepository struct {
	connection connection
}

var _ application.TimeZoneReader = (*TimeZoneRepository)(nil)

// NewTimeZoneRepository returns a TimeZoneRepository reading through pool.
func NewTimeZoneRepository(pool PoolSource) *TimeZoneRepository {
	return &TimeZoneRepository{connection: connection{source: pool}}
}

// TimeZones returns a page of time zones ordered by id.
func (repository *TimeZoneRepository) TimeZones(ctx context.Context, filter application.TimeZoneFilter) ([]domain.TimeZone, error) {
	return repository.collect(repository.connection.query(ctx, listTimeZonesQuery, filter.After, filter.Limit))
}

// TimeZonesByID returns the time zones with ids.
func (repository *TimeZoneRepository) TimeZonesByID(ctx context.Context, ids []string) ([]domain.TimeZone, error) {
	return repository.collect(repository.connection.query(ctx, timeZonesByIDQuery, ids))
}

func (*TimeZoneRepository) collect(rows pgx.Rows, err error) ([]domain.TimeZone, error) {
	if err != nil {
		return nil, err
	}
	timeZoneRows, err := pgx.CollectRows(rows, pgx.RowToStructByName[timeZoneRow])
	if err != nil {
		return nil, fmt.Errorf("geo time zones: %w", err)
	}
	timeZones := make([]domain.TimeZone, 0, len(timeZoneRows))
	for _, row := range timeZoneRows {
		timeZones = append(timeZones, row.timeZone())
	}
	return timeZones, nil
}
