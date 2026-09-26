package postgres

import (
	"context"
	"fmt"

	"github.com/jackc/pgx/v5"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// CityRepository is the Postgres application.CityReader.
type CityRepository struct {
	connection connection
}

var _ application.CityReader = (*CityRepository)(nil)

// NewCityRepository returns a CityRepository reading through pool.
func NewCityRepository(pool PoolSource) *CityRepository {
	return &CityRepository{connection: connection{source: pool}}
}

// Cities returns a page of cities ordered by id.
func (repository *CityRepository) Cities(ctx context.Context, locale i18n.Locale, filter application.CityFilter) ([]domain.City, error) {
	statements := repository.connection
	return repository.collect(statements.query(ctx, listCitiesQuery, statements.locale(locale), filter.After,
		statements.optional(filter.CountryCode), filter.SubdivisionID, filter.Limit))
}

// CitiesByID returns the cities with ids.
func (repository *CityRepository) CitiesByID(ctx context.Context, locale i18n.Locale, ids []int64) ([]domain.City, error) {
	return repository.collect(repository.connection.query(ctx, citiesByIDQuery, repository.connection.locale(locale), ids))
}

func (*CityRepository) collect(rows pgx.Rows, err error) ([]domain.City, error) {
	if err != nil {
		return nil, err
	}
	cityRows, err := pgx.CollectRows(rows, pgx.RowToStructByName[cityRow])
	if err != nil {
		return nil, fmt.Errorf("geo cities: %w", err)
	}
	cities := make([]domain.City, 0, len(cityRows))
	for _, row := range cityRows {
		cities = append(cities, row.city())
	}
	return cities, nil
}
